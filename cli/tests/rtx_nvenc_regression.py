"""实机回归：UHQ 初始化、B 帧重排和 MP4 尾帧完整性。"""

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def run(args):
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                            errors="replace", timeout=180)
    if result.returncode:
        raise RuntimeError(f"退出码 {result.returncode}: {args}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def run_expect_failure(args):
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                            errors="replace", timeout=180)
    if result.returncode == 0:
        raise AssertionError(f"预期失败但命令成功: {args}\n{result.stdout}\n{result.stderr}")
    return result.stdout + result.stderr


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", type=Path, required=True)
    args = parser.parse_args()
    core = args.core.resolve()
    ffmpeg = shutil.which("ffmpeg")
    ffprobe = shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        raise RuntimeError("PATH 中未找到 3FUI 使用的 ffmpeg/ffprobe")
    cli = str(core / "videoenhancer.exe")
    folder = Path(tempfile.mkdtemp(prefix="rtx-nvenc-regression-"))
    print(f"测试产物：{folder}", flush=True)
    # 非零起始时间覆盖减去源时间偏移时将负 DTS 截断到零的旧缺陷。
    source = folder / "offset.mkv"
    run([ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
         "testsrc2=size=640x360:rate=24", "-f", "lavfi", "-i",
         "sine=frequency=440:duration=0.2", "-f", "lavfi", "-i",
         "sine=frequency=550:duration=0.2", "-f", "lavfi", "-i",
         "sine=frequency=660:duration=0.2", "-map", "0:v:0", "-map", "1:a:0",
         "-map", "2:a:0", "-map", "3:a:0", "-frames:v", "4", "-c:v", "libx264",
         "-c:a", "aac",
         "-output_ts_offset", "10", str(source)])
    for tune in ("hq", "uhq"):
        for container in ("mkv", "mp4"):
            output = folder / f"{tune}.{container}"
            # HQ 覆盖默认 8-bit NV12；UHQ 覆盖用户实际使用的 Main10/P010。
            pixel_format = "yuv420p" if tune == "hq" else "p010le"
            settings = (f'-c:v hevc_nvenc -preset p7 -tune {tune} -pix_fmt {pixel_format} '
                        f'-rc vbr -cq 28 -map 0:v:0? -map 0:a:0? -map 0:a:2? "{output}" -y')
            cli_output = run([cli, "-i", str(source), "-backend", "rtxvsr", "-rtx-target", "2x",
                              "-rtx-quality", "4", "-ffmpeg-settings", settings])
            assert "[RTX Video] 最终编码：3FUI FFmpeg" in cli_output, cli_output
            totals = [line.rsplit(":", 1)[-1].strip() for line in cli_output.splitlines()
                      if line.startswith("Total Output Frames:")]
            assert totals and totals[-1] == "4", cli_output
            data = json.loads(run([ffprobe, "-v", "error", "-select_streams", "v:0",
                                   "-count_frames", "-show_streams", "-show_packets",
                                   "-of", "json", str(output)]))
            stream = data["streams"][0]
            packets = data["packets"]
            assert int(stream["nb_read_frames"]) == 4, data
            assert (stream["width"], stream["height"]) == (1280, 720), data
            if tune == "uhq":
                assert "10" in stream["pix_fmt"], data
                assert stream.get("profile") == "Main 10", data
            else:
                assert "10" not in stream["pix_fmt"], data
            assert len(packets) == 4, data
            assert all("D" not in p.get("flags", "") for p in packets), data
            assert all(int(p.get("duration", 0)) > 0 for p in packets), data
            dts = [int(p["dts"]) for p in packets if "dts" in p]
            assert all(a < b for a, b in zip(dts, dts[1:])), data
            all_streams = json.loads(run([ffprobe, "-v", "error", "-show_streams",
                                          "-of", "json", str(output)]))["streams"]
            audio = [item for item in all_streams if item["codec_type"] == "audio"]
            assert len(audio) == 2, all_streams
            print(f"PASS {tune}/{container}: {stream['pix_fmt']}，解码 4/4 帧，无 DISCARD，DTS 单调", flush=True)

    # RTX 仅生产原始帧后，软件编码器应走同一个宿主 FFmpeg 管线。
    for encoder, codec_name, pixel_format, encoder_options in (
            ("libx264", "h264", "yuv420p", "-preset ultrafast -crf 28"),
            ("libx265", "hevc", "yuv420p10le", "-preset ultrafast -crf 28"),
            ("libsvtav1", "av1", "yuv420p10le", "-preset 12 -crf 40")):
        output = folder / f"{encoder}.mkv"
        settings = (f'-c:v {encoder} {encoder_options} -pix_fmt {pixel_format} '
                    f'-map 0:v:0? -map 0:a:0? "{output}" -y')
        cli_output = run([cli, "-i", str(source), "-backend", "rtxvsr", "-rtx-target", "2x",
                          "-rtx-quality", "4", "-ffmpeg-settings", settings])
        assert "[RTX Video] 最终编码：3FUI FFmpeg" in cli_output, cli_output
        streams = json.loads(run([ffprobe, "-v", "error", "-count_frames", "-show_streams",
                                  "-of", "json", str(output)]))["streams"]
        video = next(item for item in streams if item["codec_type"] == "video")
        assert video["codec_name"] == codec_name, streams
        assert int(video["nb_read_frames"]) == 4, streams
        assert (video["width"], video["height"]) == (1280, 720), streams
        if encoder == "libx265":
            assert "10" in video["pix_fmt"] and video.get("profile") == "Main 10", streams
        elif encoder == "libsvtav1":
            assert "10" in video["pix_fmt"], streams
        else:
            assert "10" not in video["pix_fmt"], streams
        assert len([item for item in streams if item["codec_type"] == "audio"]) == 1, streams
        print(f"PASS RTX + {encoder}: {video['pix_fmt']}，4/4 帧，宿主 FFmpeg 软件编码", flush=True)

    # MKV 额外覆盖字幕类型内序号：三条字幕只保留第 1、3 条，同时音频只保留第 2 条。
    subtitle_files = []
    for index, text in enumerate(("alpha", "beta", "gamma")):
        path = folder / f"subtitle-{index}.srt"
        path.write_text(f"1\n00:00:00,000 --> 00:00:00,150\n{text}\n", encoding="utf-8")
        subtitle_files.append(path)
    subtitle_source = folder / "offset-with-subtitles.mkv"
    subtitle_mux = [ffmpeg, "-hide_banner", "-loglevel", "error", "-i", str(source)]
    for path in subtitle_files:
        subtitle_mux.extend(["-i", str(path)])
    subtitle_mux.extend(["-map", "0", "-map", "1:0", "-map", "2:0", "-map", "3:0",
                         "-c", "copy", str(subtitle_source)])
    run(subtitle_mux)
    selected_output = folder / "selected-streams.mkv"
    selected_settings = (f'-c:v hevc_nvenc -preset p7 -tune hq -map 0:v:0? -map 0:a:1? '
                         f'-map 0:s:0? -map 0:s:2? "{selected_output}" -y')
    run([cli, "-i", str(subtitle_source), "-backend", "rtxvsr", "-rtx-target", "2x",
         "-rtx-quality", "4", "-ffmpeg-settings", selected_settings])
    selected_streams = json.loads(run([ffprobe, "-v", "error", "-show_streams", "-of",
                                       "json", str(selected_output)]))["streams"]
    assert len([item for item in selected_streams if item["codec_type"] == "audio"]) == 1, selected_streams
    assert len([item for item in selected_streams if item["codec_type"] == "subtitle"]) == 2, selected_streams
    print("PASS -map：3 音轨选 1、3 字幕选 2，类型内序号正确", flush=True)

    invalid_output = folder / "h264-p010-invalid.mkv"
    invalid = run_expect_failure([
        cli, "-i", str(source), "-backend", "rtxvsr", "-rtx-target", "2x",
        "-rtx-quality", "4", "-ffmpeg-settings",
        f'-c:v h264_nvenc -pix_fmt p010le "{invalid_output}" -y'])
    assert "H.264 NVENC does not support the requested 10-bit output" in invalid, invalid
    assert not invalid_output.exists(), invalid_output
    print("PASS h264/p010 门禁：明确拒绝且未留下输出文件", flush=True)

    # 小于常见 D3D11VA 解码下限的素材应改走软件解码后上传 D3D11，
    # 同时覆盖 8-bit H.264 与 10-bit HEVC 两种常见输入。
    for source_codec, source_pixel_format in (
            ("libx264", "yuv420p"),
            ("libx265", "yuv420p10le")):
        low_source = folder / f"low-192x128-{source_codec}.mkv"
        low_output = folder / f"low-192x128-{source_codec}-rtx.mkv"
        run([ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
             "testsrc2=size=192x128:rate=4", "-frames:v", "4", "-c:v", source_codec,
             "-preset", "ultrafast", "-pix_fmt", source_pixel_format, str(low_source), "-y"])
        low_settings = (f'-c:v hevc_nvenc -preset p4 -pix_fmt p010le '
                        f'"{low_output}" -y')
        run([cli, "-i", str(low_source), "-backend", "rtxvsr", "-rtx-target", "2x",
             "-rtx-quality", "3", "-ffmpeg-settings", low_settings])
        low_stream = json.loads(run([
            ffprobe, "-v", "error", "-select_streams", "v:0", "-count_frames",
            "-show_streams", "-of", "json", str(low_output)]))["streams"][0]
        assert (low_stream["width"], low_stream["height"]) == (384, 256), low_stream
        assert int(low_stream["nb_read_frames"]) == 4, low_stream
        assert "10" in low_stream["pix_fmt"], low_stream
        hashes = run([ffmpeg, "-v", "error", "-i", str(low_output), "-f", "framemd5", "-"])
        frame_hashes = [line.rsplit(",", 1)[-1].strip() for line in hashes.splitlines()
                        if line and not line.startswith("#")]
        assert len(frame_hashes) == 4 and len(set(frame_hashes)) == 4, frame_hashes
        print(f"PASS 低分辨率 {source_codec}: 192x128→384x256，10-bit 4/4 帧且帧内容不同", flush=True)


if __name__ == "__main__":
    main()
