Imports System
Imports System.Drawing
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports LakeUI
Imports videoenhancer

Module Program
    <STAThread>
    Sub Main(args As String())
        Using preview As New PixelPictureBox(), first As New Bitmap(64, 48), second As New Bitmap(128, 96)
            PixelPreviewImage.SetImage(preview, first)
            CheckSize(preview, 64, 48)
            PixelPreviewImage.SetImage(preview, second)
            CheckSize(preview, 128, 96)
            PixelPreviewImage.SetImage(preview, Nothing)
            Dim propertyInfo = GetType(PixelPictureBox).GetProperty("Image")
            If propertyInfo Is Nothing Then propertyInfo = GetType(PixelPictureBox).GetProperty("Source")
            If propertyInfo.GetValue(preview) IsNot Nothing Then Throw New Exception("Preview not cleared")
            If second.GetPixel(0, 0) <> Color.FromArgb(0) Then Throw New Exception("Caller-owned image was disposed")
        End Using
        If args.Length > 0 Then CheckPluginCallback(args(0))
        Console.WriteLine("PIXEL_PREVIEW_PASS|" & GetType(PixelPictureBox).Assembly.GetName().Version.ToString() & "|assign|replace|clear|ownership")
    End Sub

    Private Sub CheckPluginCallback(pluginPath As String)
        Dim panelType = Assembly.LoadFrom(pluginPath).GetType("videoenhancer.PluginPanel", throwOnError:=True)
        Dim panel = RuntimeHelpers.GetUninitializedObject(panelType)
        Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
        Using preview As New PixelPictureBox(), first As New Bitmap(64, 48), second As New Bitmap(128, 96)
            panelType.GetField("_picPreview", flags).SetValue(panel, preview)
            Dim callback = panelType.GetMethod("OnPreviewFrameReady", flags)
            callback.Invoke(panel, {Nothing, first})
            CheckSize(preview, 64, 48)
            callback.Invoke(panel, {Nothing, second})
            CheckSize(preview, 128, 96)
            If Not ReferenceEquals(panelType.GetField("_lastPreviewImage", flags).GetValue(panel), second) Then Throw New Exception("Frame callback did not store the image")
            PixelPreviewImage.SetImage(preview, Nothing)
        End Using
        Console.WriteLine("PLUGIN_CALLBACK_PASS|OnPreviewFrameReady|replace")
    End Sub

    Private Sub CheckSize(preview As PixelPictureBox, width As Integer, height As Integer)
        Dim propertyInfo = GetType(PixelPictureBox).GetProperty("Image")
        If propertyInfo IsNot Nothing Then
            Dim image = DirectCast(propertyInfo.GetValue(preview), Image)
            If image.Width <> width OrElse image.Height <> height Then Throw New Exception("Image size mismatch")
        Else
            Dim source = GetType(PixelPictureBox).GetProperty("Source").GetValue(preview)
            If CLng(source.GetType().GetProperty("Width").GetValue(source)) <> width OrElse
               CLng(source.GetType().GetProperty("Height").GetValue(source)) <> height Then Throw New Exception("Source size mismatch")
        End If
    End Sub
End Module
