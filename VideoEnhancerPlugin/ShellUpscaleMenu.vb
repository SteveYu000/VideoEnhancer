Imports System
Imports System.Collections.Generic
Imports System.IO
Imports Microsoft.Win32

Namespace videoenhancer

    Friend NotInheritable Class ShellUpscaleMenu
        Private Const MenuKey As String = "Software\Classes\SystemFileAssociations\image\shell\VideoEnhancer.Upscale"

        Private Sub New()
        End Sub

        Public Shared Sub Apply(exePath As String, models As IEnumerable(Of ShellUpscaleModel))
            If Not File.Exists(exePath) Then Throw New FileNotFoundException("找不到 videoenhancer.exe", exePath)
            Dim entries = New List(Of ShellUpscaleModel)(models)
            If entries.Count = 0 Then Throw New InvalidOperationException("请至少添加一个右键超分模型")

            Remove()
            Using root = Registry.CurrentUser.CreateSubKey(MenuKey, True)
                If root Is Nothing Then Throw New InvalidOperationException("无法创建当前用户右键菜单注册表项")
                root.SetValue("MUIVerb", "超分辨率", RegistryValueKind.String)
                root.SetValue("Icon", exePath, RegistryValueKind.String)
                root.SetValue("SubCommands", "", RegistryValueKind.String)
                Using shell = root.CreateSubKey("shell", True)
                    For index = 0 To entries.Count - 1
                        Dim entry = entries(index)
                        Dim keyName = (index + 1).ToString("000")
                        Using item = shell.CreateSubKey(keyName, True)
                            item.SetValue("MUIVerb", If(String.IsNullOrWhiteSpace(entry.DisplayName), Path.GetFileName(entry.Model), entry.DisplayName), RegistryValueKind.String)
                            item.SetValue("Icon", exePath, RegistryValueKind.String)
                            Using command = item.CreateSubKey("command", True)
                                command.SetValue("", Quote(exePath) &
                                    " --image-input ""%1"" --image-output-original --image-suffix model --image-png" &
                                    " -backend " & Quote(entry.Backend) & " -modelpath " & Quote(entry.Model), RegistryValueKind.String)
                            End Using
                        End Using
                    Next
                End Using
            End Using
        End Sub

        Public Shared Sub Remove()
            Using classes = Registry.CurrentUser.OpenSubKey("Software\Classes\SystemFileAssociations\image\shell", True)
                If classes IsNot Nothing Then classes.DeleteSubKeyTree("VideoEnhancer.Upscale", False)
            End Using
        End Sub

        Private Shared Function Quote(value As String) As String
            Return """" & value.Replace("""", "\""") & """"
        End Function
    End Class

End Namespace
