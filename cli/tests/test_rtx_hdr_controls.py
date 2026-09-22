import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PLUGIN = ROOT / "VideoEnhancerPlugin"
CLI = ROOT / "cli"


def read_plugin_panel_sources():
    paths = [PLUGIN / "PluginPanel.vb"]
    paths.extend(sorted((PLUGIN / "Pages").glob("PluginPanel.*Page.vb")))
    return "\n".join(path.read_text(encoding="utf-8-sig") for path in paths)


class RtxHdrAndQueueCompatibilityTests(unittest.TestCase):
    def test_hdr_config_defaults_and_clamp_contract(self):
        config = (PLUGIN / "PluginConfig.vb").read_text(encoding="utf-8-sig")
        expected = {
            "RtxHdrContrast": "100",
            "RtxHdrSaturation": "100",
            "RtxHdrMiddleGray": "44",
            "RtxHdrMaxLuminance": "1000",
        }
        for name, default in expected.items():
            self.assertRegex(config, rf"Property {name} As Integer = {default}")
        self.assertIn("ClampRtxHdrContrast", config)
        self.assertIn("ClampRtxHdrSaturation", config)
        self.assertIn("ClampRtxHdrMiddleGray", config)
        self.assertIn("ClampRtxHdrMaxLuminance", config)

    def test_hdr_ui_uses_editable_integer_numeric_controls_and_layout(self):
        panel = read_plugin_panel_sources()
        self.assertEqual(4, panel.count("New RtxHdrNumericUpDown()"))
        self.assertIn("Inherits ModernNumericUpDown", panel)
        self.assertIn("Protected Overrides Sub OnMouseWheel", panel)
        self.assertIn("Case Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown", panel)
        self.assertIn("Protected Overrides Sub OnSizeChanged", panel)
        self.assertIn("ResetViewportDuringLayout", panel)
        self.assertGreaterEqual(panel.count("ConfigureRtxHdrNumeric("), 4)
        self.assertIn("control.DecimalPlaces = 0", panel)
        self.assertIn("control.Editable = True", panel)
        self.assertIn("control.Size = New Size(320, 34)", panel)
        self.assertIn("control.ButtonAreaWidth = 1", panel)
        self.assertIn("control.DividerSize = 0", panel)
        self.assertIn("control.Padding = New Padding(10, 0, 10, 0)", panel)
        self.assertIn("_config.Enabled AndAlso _config.RtxHdrEnabled", panel)
        self.assertIn("_rtxHdrContrastField, 744", panel)
        self.assertIn("_rtxHdrMaxLuminanceField, 814", panel)
        self.assertIn("Dim rootTop As Integer = root.Top", panel)
        self.assertIn("root.SetBounds(rootLeft, rootTop, width, UpscaleContentHeight)", panel)
        self.assertNotIn("root.SetBounds(0, 0, width, UpscaleContentHeight)", panel)

    def test_cli_validates_all_hdr_ranges_and_serializes_json_fields(self):
        program = (CLI / "Program.cs").read_text(encoding="utf-8-sig")
        client = (CLI / "RtxVideoBackendClient.cs").read_text(encoding="utf-8-sig")
        ranges = {
            "rtxHdrContrast": "0 or > 200",
            "rtxHdrSaturation": "0 or > 200",
            "rtxHdrMiddleGray": "10 or > 100",
            "rtxHdrMaxLuminance": "400 or > 2000",
        }
        for variable, bound in ranges.items():
            self.assertIn(f"{variable} is < {bound}", program)
        for option in (
            "--rtx-hdr-contrast",
            "--rtx-hdr-saturation",
            "--rtx-hdr-middle-gray",
            "--rtx-hdr-max-luminance",
        ):
            self.assertIn(option, program)
        for field in ("contrast", "saturation", "middleGray", "maxLuminance"):
            self.assertRegex(client, rf'WriteNumber\("{field}", hdr')

    def test_queue_access_does_not_bind_old_list_getter(self):
        sources = [
            path.read_text(encoding="utf-8-sig")
            for path in PLUGIN.rglob("*.vb")
            if "bin" not in path.parts and "obj" not in path.parts
        ]
        joined = "\n".join(sources)
        self.assertNotIn("编码队列_v6.队列", joined)
        adapter = (PLUGIN / "HostQueueAccess.vb").read_text(encoding="utf-8-sig")
        self.assertIn('"获取队列快照"', adapter)
        self.assertIn('"根据ID获取任务"', adapter)
        self.assertIn('GetProperty("队列"', adapter)

    def test_update_download_prefers_modelscope_and_keeps_github_manifest_priority(self):
        updater = (PLUGIN / "PluginUpdater.vb").read_text(encoding="utf-8-sig")
        manifest = updater.split(
            "Public Shared Async Function FetchLatestManifestAsync", 1
        )[1].split("Private Shared Async Function FetchGithubManifestAsync", 1)[0]
        self.assertLess(
            manifest.index("Return Await FetchGithubManifestAsync"),
            manifest.index("Return Await FetchModelScopeManifestAsync"),
        )
        download = updater.split(
            "Public Shared Async Function DownloadPackageAsync", 1
        )[1].split("Public Shared Sub StartUpdate", 1)[0]
        self.assertLess(
            download.index("DownloadToFileAsync(BuildResolveUrl(manifest.Package.Path)"),
            download.index("DownloadToFileAsync(githubUrl"),
        )
        self.assertIn("更新包优先走 ModelScope", download)

    def test_update_launches_wix_burn_and_forwards_portable_install_root(self):
        updater = (PLUGIN / "PluginUpdater.vb").read_text(encoding="utf-8-sig")
        bundle = (ROOT / "installer" / "Bundle" / "Bundle.wxs").read_text(
            encoding="utf-8-sig"
        )
        package = (ROOT / "installer" / "Package" / "Package.wxs").read_text(
            encoding="utf-8-sig"
        )
        theme = (ROOT / "installer" / "Bundle" / "VideoEnhancerTheme.xml").read_text(
            encoding="utf-8-sig"
        )
        localization = (
            ROOT / "installer" / "Bundle" / "VideoEnhancerTheme.zh-CN.wxl"
        ).read_text(encoding="utf-8-sig")
        program = (CLI / "Program.cs").read_text(encoding="utf-8-sig")
        self.assertIn("Return remoteVersion > installedVersion", updater)
        self.assertIn(".UseShellExecute = True", updater)
        self.assertIn(
            'startInfo.ArgumentList.Add("InstallFolder=" & hostRoot)', updater
        )
        self.assertIn('Name="InstallFolder"', bundle)
        self.assertNotIn('[ProgramFiles64Folder]FFmpegFreeUI', bundle)
        self.assertIn('Variable="InstallFolder"', bundle)
        self.assertIn('Value="InstallRoot"', bundle)
        self.assertIn('Value=""', bundle)
        self.assertIn('Condition="NOT InstallFolder"', bundle)
        self.assertIn('VisibleCondition="InstallFolder"', theme)
        self.assertIn(
            '<BrowseDirectoryAction VariableName="InstallFolder"', theme
        )
        self.assertIn('Culture="zh-CN" Language="2052"', localization)
        self.assertIn("首次安装必须先选择包含 FFmpegFreeUI.exe", localization)
        self.assertIn('Persisted="yes"', bundle)
        self.assertIn('bal:Overridable="yes"', bundle)
        self.assertIn('<MsiProperty Name="THREEFUIROOT"', bundle)
        self.assertIn('<RemoveRegistryKey Id="RemoveVideoEnhancerRegistryKey"', package)
        self.assertIn('Id="CleanupCurrentUserRegistryResidue"', package)
        self.assertIn('ExeCommand="--cleanup-registry-residue"', package)
        self.assertIn('case "--cleanup-registry-residue"', program)
        self.assertIn('DeleteSubKeyTree("VideoEnhancer.Upscale"', program)
        self.assertIn('ExeCommand="[CustomActionData]"', package)
        for obsolete in (
            "--create-installer-bundle",
            "--apply-update",
            "--update-package",
            "--update-target",
            "--wait-pid",
            "--restart-exe",
        ):
            self.assertNotIn(obsolete, program)
            self.assertNotIn(obsolete, updater)

    def test_stop_graceful_then_force_contract(self):
        stop = (PLUGIN / "StopControl.vb").read_text(encoding="utf-8-sig")
        cli = (CLI / "Program.cs").read_text(encoding="utf-8-sig")
        self.assertIn("GracefulStopTimeoutSeconds As Double = 12.0", stop)
        self.assertIn('"停止任务"', stop)
        self.assertIn("task.手动停止 = True", stop)
        self.assertIn("OutputMarkerSuffix", stop)
        self.assertIn("CleanupInvalidStoppedOutput", stop)
        self.assertIn("WaitForExit(8000)", cli)
        self.assertIn("WriteGracefulStopMarker", cli)
        self.assertIn("finalOutputCompleted = true", cli)


if __name__ == "__main__":
    unittest.main()
