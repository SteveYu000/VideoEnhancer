VideoEnhancer 手动安装包
========================

本包不注册 MSI 产品，也不写入注册表。本包内的目录结构与 3FUI 安装布局一致：

先完全退出 3FUI。
把解压出的 plugin 文件夹整体复制到 3FUI 根目录（与 FFmpegFreeUI.exe 同级），
如提示已有同名文件选择覆盖即可。完成后启动 3FUI，在插件里执行一次环境检查。
插件会固定使用 plugin\videoenhancer\videoenhancer.exe；请保持包内目录结构，
无需也不能另外指定 EXE 或核心资源目录。配置与运行缓存都保存在该 videoenhancer 目录内。

如果从旧版平铺目录升级，可在 3FUI 根目录打开 PowerShell，复制完成后执行：

& .\plugin\videoenhancer\videoenhancer.exe --cleanup-legacy-residue `
  --plugin-root .\plugin `
  --legacy-local-app-data $env:LOCALAPPDATA `
  --legacy-temp-root $env:TEMP

该命令会把旧 plugin\bin、models、python、缓存和更新目录安全合并到
plugin\videoenhancer；同名不同内容不会覆盖，并会保留在原位置供人工处理。
它还会迁移旧 AppData 配置、删除旧 INI 和已知临时更新器残留。

请勿删除 bin\aria2-next：它是以独立进程运行的 GPL 下载组件。
第三方组件声明位于 THIRD-PARTY-NOTICES.txt，许可证、作者声明与精确来源位于 licenses 目录。

模型与后端（Python 环境等）体积较大，不在本包内；
装好插件后在模型下载页按需获取。
