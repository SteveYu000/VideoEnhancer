Imports System
Imports System.Drawing
Imports System.Linq
Imports System.Linq.Expressions
Imports System.Reflection
Imports LakeUI

Namespace videoenhancer
    ''' <summary>兼容旧版 Image 和新版 Source 入口，不直接链接已移除的 setter。</summary>
    Friend NotInheritable Class PixelPreviewImage
        Private Shared ReadOnly ImageProperty As PropertyInfo = GetType(PixelPictureBox).GetProperty("Image")
        Private Shared ReadOnly SourceProperty As PropertyInfo = GetType(PixelPictureBox).GetProperty("Source")
        Private Shared ReadOnly SourceConstructor As ConstructorInfo = FindSourceConstructor()
        Private Shared ReadOnly CreateRenderer As Func(Of Image, Object) = BuildRendererFactory()

        Private Shared Function FindSourceConstructor() As ConstructorInfo
            If ImageProperty IsNot Nothing Then Return Nothing
            Dim sourceType = GetType(PixelPictureBox).Assembly.GetType("LakeUI.PixelPictureCallbackSource", throwOnError:=True)
            Return sourceType.GetConstructors().Single()
        End Function

        Private Shared Function BuildRendererFactory() As Func(Of Image, Object)
            If ImageProperty IsNot Nothing Then Return Nothing
            Dim delegateType = SourceConstructor.GetParameters()(2).ParameterType
            Dim parameters = delegateType.GetMethod("Invoke").GetParameters().Select(Function(item) Expression.Parameter(item.ParameterType, item.Name)).ToArray()
            Dim image = Expression.Parameter(GetType(Image), "image")
            Dim rectangle = Expression.Call(parameters(2), parameters(2).Type.GetMethod("ToRectangleF"))
            Dim draw = parameters(0).Type.GetMethods().Single(Function(item) item.Name = "DrawImage" AndAlso item.GetParameters().Length = 6)
            Dim render = Expression.Call(parameters(0), draw, image, parameters(1),
                Expression.Convert(rectangle, GetType(Nullable(Of RectangleF))),
                Expression.Constant(1.0F), parameters(3), parameters(4))
            Dim callback = Expression.Lambda(delegateType, Expression.Block(render, Expression.Constant(True)), parameters)
            Return Expression.Lambda(Of Func(Of Image, Object))(Expression.Convert(callback, GetType(Object)), image).Compile()
        End Function

        Friend Shared Sub SetImage(control As PixelPictureBox, image As Image)
            If ImageProperty IsNot Nothing Then
                ImageProperty.SetValue(control, image)
                Return
            End If
            If image Is Nothing Then
                SourceProperty.SetValue(control, Nothing)
                Return
            End If
            Dim arguments = SourceConstructor.GetParameters().Select(Function(item) If(item.HasDefaultValue, item.DefaultValue, Nothing)).ToArray()
            arguments(0) = CLng(image.Width)
            arguments(1) = CLng(image.Height)
            arguments(2) = CreateRenderer(image)
            SourceProperty.SetValue(control, SourceConstructor.Invoke(arguments))
        End Sub
    End Class
End Namespace
