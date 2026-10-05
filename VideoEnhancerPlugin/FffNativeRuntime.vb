Imports System
Imports System.IO
Imports System.ComponentModel
Imports System.Runtime.InteropServices

Namespace videoenhancer
    ''' <summary>从完整分发包安装的目录加载 API 11；不在运行时释放或下载 DLL。</summary>
    Friend Module FffNativeRuntime
        <DllImport("kernel32.dll", EntryPoint:="LoadLibraryExW", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Private Function LoadLibraryEx(path As String, reserved As IntPtr, flags As UInteger) As IntPtr
        End Function

        Friend Function LoadLibrary() As IntPtr
            Dim folder = Path.Combine(PortableRuntime.ApplicationRoot, "bin", "fff-native-11")
            For Each name In {"FFF.Native.dll", "ass-9.dll", "brotlicommon.dll", "brotlidec.dll", "bz2.dll",
                              "freetype.dll", "fribidi-0.dll", "harfbuzz.dll", "libpng16.dll", "z.dll"}
                Dim filePath = Path.Combine(folder, name)
                If Not File.Exists(filePath) OrElse New FileInfo(filePath).Length = 0 Then
                    Throw New FileNotFoundException("缺少预览运行组件，请用完整 ZIP 或安装器修复安装：" & filePath, filePath)
                End If
            Next
            ' 优先解析同目录动态依赖，允许用户依法替换 LGPL 库，不限制其 DLL 哈希。
            Dim handle = LoadLibraryEx(Path.Combine(folder, "FFF.Native.dll"), IntPtr.Zero, &H1100UI)
            If handle = IntPtr.Zero Then Throw New Win32Exception(Marshal.GetLastWin32Error(), "无法加载 FFF.Native 预览组件。")
            Return handle
        End Function
    End Module
End Namespace
