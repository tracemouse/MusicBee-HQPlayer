Imports System.Text
Imports System.IO
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Security
Imports System.Runtime.InteropServices
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

Partial Public Class Plugin
    Private MustInherit Class Encoder
    End Class  ' Encoder

    <SuppressUnmanagedCodeSecurity()> _
    Private NotInheritable Class AudioEncoder
        Inherits Encoder
        Public Codec As FileCodec
        Private Shared isBassMixLoaded As Boolean = False

        Shared Sub New()
            Sockets_Encoder_Init(Application.StartupPath & "\bass.dll", Application.StartupPath & "\bassenc.dll", Application.StartupPath & "\bassmix.dll")
        End Sub

        Public Sub New(codec As FileCodec)
            Me.Codec = codec
        End Sub

        Public Function GetEncodeStreamHandle(streamHandle As Integer, encodeSampleRate As Integer, encodeChannelCount As Integer, encodeBitDepth As Integer, alwaysCreateMixStream As Boolean) As Integer
            Dim sampleRate As Integer
            Dim channelCount As Integer
            Dim streamCodec As FileCodec
            Bass.TryGetStreamInformation(streamHandle, sampleRate, channelCount, streamCodec)
            If encodeChannelCount <> channelCount OrElse encodeSampleRate <> sampleRate OrElse alwaysCreateMixStream Then
                If Not isBassMixLoaded Then
                    isBassMixLoaded = True
                    LoadLibraryEx(Application.StartupPath & "\bassmix.dll", IntPtr.Zero, &H8)
                End If
                Dim sourceHandle As Integer = streamHandle
                streamHandle = BASS_Mixer_StreamCreate(encodeSampleRate, encodeChannelCount, (Bass.BASSFlag.BASS_STREAM_DECODE Or Bass.BASSFlag.BASS_SAMPLE_FLOAT))
                Dim mixFlags As Bass.BASSFlag = (Bass.BASSFlag.BASS_STREAM_AUTOFREE Or Bass.BASSFlag.BASS_MIXER_NORAMPIN)
                If channelCount = 1 AndAlso encodeChannelCount > 1 Then
                    mixFlags = mixFlags Or Bass.BASSFlag.BASS_MIXER_MATRIX
                ElseIf channelCount > 2 AndAlso encodeChannelCount = 2 Then
                    mixFlags = mixFlags Or Bass.BASSFlag.BASS_MIXER_DOWNMIX
                End If
                BASS_Mixer_StreamAddChannel(streamHandle, sourceHandle, mixFlags)
                If channelCount = 1 AndAlso encodeChannelCount > 1 Then
                    Dim matrix(1, 0) As Single
                    matrix(0, 0) = 0.5F
                    matrix(1, 0) = 0.5F
                    BASS_Mixer_ChannelSetMatrix(sourceHandle, matrix)
                End If
            End If
            Return streamHandle
        End Function

        Public Sub StartEncode(url As String, streamHandle As Integer, isPartialContent As Boolean, fileEncodeLength As Long, encodeBitDepth As Integer, targetSocketHandle As IntPtr, logId As String)
            Try
                Dim is16BitOutput As Boolean = (encodeBitDepth <> 24 OrElse (Codec <> FileCodec.Pcm AndAlso Codec <> FileCodec.Wave))
                Dim flags As BASSEncode
                Dim encoderCommandLine As String
                Select Case Codec
                    Case FileCodec.Pcm, FileCodec.Wave
                        flags = (If(is16BitOutput, BASSEncode.BASS_ENCODE_FP_16BIT, BASSEncode.BASS_ENCODE_FP_24BIT) Or BASSEncode.BASS_ENCODE_PCM)
                        If Codec = FileCodec.Pcm Then
                            flags = (flags Or BASSEncode.BASS_ENCODE_NOHEAD Or BASSEncode.BASS_ENCODE_BIGEND)
                        ElseIf isPartialContent Then
                            flags = (flags Or BASSEncode.BASS_ENCODE_NOHEAD)
                        End If
                        encoderCommandLine = Nothing
                    Case FileCodec.Mp3, FileCodec.Aac, FileCodec.Ogg
                        flags = BASSEncode.BASS_ENCODE_FP_16BIT
                        encoderCommandLine = mbApiInterface.Setting_GetFileConvertCommandLine(Codec, EncodeQuality.HighQuality).Replace("[outputfile]", "-")
                End Select

                Dim startTime As Long
                Dim playTime As Long
                Dim errorCode As Integer
                sendDataBarrier.Wait()
                Try
                    startTime = DateTime.UtcNow.Ticks
                    errorCode = Sockets_Encoder_Start(streamHandle, CUInt(fileEncodeLength), encoderCommandLine, (flags Or BASSEncode.BASS_UNICODE), targetSocketHandle)
                    playTime = (DateTime.UtcNow.Ticks - startTime) \ TimeSpan.TicksPerMillisecond
                Finally
                    sendDataBarrier.Release()
                End Try
                If Settings.LogDebugInfo Then
                    LogInformation(logId, "exit=" & errorCode & ", playtime=" & playTime)
                End If
            Catch ex As Exception
                LogError(ex, logId)
            Finally
                Bass.CloseStream(streamHandle)
            End Try
        End Sub

        Public Shared Sub StopEncode()
            Sockets_Encoder_Stop()
        End Sub

        Private Enum BASSEncode
            BASS_ENCODE_AUTOFREE = &H40000
            BASS_ENCODE_BIGEND = &H10
            BASS_ENCODE_DEFAULT = 0
            BASS_ENCODE_FP_16BIT = 4
            BASS_ENCODE_FP_24BIT = 6
            BASS_ENCODE_FP_32BIT = 8
            BASS_ENCODE_NOHEAD = 1
            BASS_ENCODE_PAUSE = &H20
            BASS_ENCODE_PCM = &H40
            BASS_ENCODE_LIMIT = &H2000
            BASS_UNICODE = -2147483648
        End Enum  ' BASSEncode

        <DllImport("kernel32.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function LoadLibraryEx(dllFilePath As String, hFile As IntPtr, dwFlags As UInteger) As IntPtr
        End Function
        <DllImport("bassmix.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_Mixer_StreamCreate(freq As Integer, chans As Integer, flags As Bass.BASSFlag) As Integer
        End Function
        <DllImport("bassmix.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_Mixer_StreamAddChannel(handle As Integer, channel As Integer, flags As Bass.BASSFlag) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
        <DllImport("bassmix.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_Mixer_ChannelSetMatrix(handle As Integer, <[In]()> matrix(,) As Single) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
        <DllImport("MusicBeeBass.dll", CallingConvention:=CallingConvention.Cdecl)> _
        Private Shared Function Sockets_Encoder_Init(<MarshalAs(UnmanagedType.LPWStr)> bassDll As String, <MarshalAs(UnmanagedType.LPWStr)> bassEncDll As String, <MarshalAs(UnmanagedType.LPWStr)> bassMixDll As String) As <MarshalAs(UnmanagedType.U1)> Boolean
        End Function
        <DllImport("MusicBeeBass.dll", CallingConvention:=CallingConvention.Cdecl)> _
        Private Shared Function Sockets_Encoder_Start(streamHandle As Integer, limit As UInteger, <MarshalAs(UnmanagedType.LPWStr)> commandLine As String, flags As Integer, socketHandle As IntPtr) As Integer
        End Function
        <DllImport("MusicBeeBass.dll", CallingConvention:=CallingConvention.Cdecl)> _
        Private Shared Sub Sockets_Encoder_Stop()
        End Sub
    End Class  ' AudioEncoder

    Private NotInheritable Class ImageEncoder
        Inherits Encoder
        Public Codec As GdiCodec
        Private ReadOnly targetSize As Integer

        Public Sub New(codec As GdiCodec, targetSize As Integer)
            Me.Codec = codec
            Me.targetSize = targetSize
        End Sub

        Public Shared Function TryCreate(codec As String, size As String) As ImageEncoder
            Dim codecEnum As GdiCodec = If(String.Compare(codec, "png", StringComparison.OrdinalIgnoreCase) = 0, GdiCodec.Png, GdiCodec.Jpeg)
            Dim sizeValue As UShort
            Select Case size
                Case "JPEG_SM_ICO"
                    sizeValue = 48
                Case "JPEG_LRG_ICO"
                    sizeValue = 120
                Case "JPEG_TN"
                    sizeValue = 160
                Case "JPEG_SM", "PNG_LRG"
                    sizeValue = 400
                Case Else
                    If Not UShort.TryParse(size, sizeValue) Then
                        sizeValue = 160
                    End If
            End Select
            Return New ImageEncoder(codecEnum, sizeValue)
        End Function

        Public Function GetMime() As String
            Select Case Codec
                Case GdiCodec.Jpeg
                    Return "image/jpeg"
                Case GdiCodec.Png
                    Return "image/png"
                Case Else
                    Return String.Empty
            End Select
        End Function

        'Private Function GetDlnaType() As String
        '    Select Case Codec
        '        Case GdiCodec.Jpeg
        '            If targetSize <= 0 Then
        '                Return "DLNA.ORG_PN=JPEG_TN;"
        '            ElseIf targetSize <= 48 Then
        '                Return "DLNA.ORG_PN=JPEG_SM_ICO;"
        '            ElseIf targetSize <= 120 Then
        '                Return "DLNA.ORG_PN=JPEG_LRG_ICO;"
        '            ElseIf targetSize <= 160 Then
        '                Return "DLNA.ORG_PN=JPEG_TN;"
        '            ElseIf targetSize <= 640 Then
        '                Return "DLNA.ORG_PN=JPEG_SM;"
        '            ElseIf targetSize <= 1024 Then
        '                Return "DLNA.ORG_PN=JPEG_MED;"
        '            Else
        '                Return "DLNA.ORG_PN=JPEG_LRG;"
        '            End If
        '        Case GdiCodec.Png
        '            Return "DLNA.ORG_PN=PNG_LRG;"
        '        Case Else
        '            Return String.Empty
        '    End Select
        'End Function

        Public Sub StartEncode(output As IO.Stream, pictureUrl As String)
            Try
                Using sourceImage As New Bitmap(pictureUrl)
                    StartEncode(sourceImage, output)
                End Using
            Catch
                LogInformation("StartEncode", "Invalid picture: " & pictureUrl)
                Throw New HttpException(404, "Bad parameter")
            End Try
        End Sub

        Private Sub StartEncode(sourceImage As Bitmap, output As IO.Stream)
            Dim imageFormat As Imaging.ImageFormat
            Select Case Codec
                Case GdiCodec.Png
                    imageFormat = Imaging.ImageFormat.Png
                Case Else
                    imageFormat = Imaging.ImageFormat.Jpeg
            End Select
            Dim imageSize As Size
            If sourceImage.Width > sourceImage.Height Then
                imageSize.Width = targetSize
                imageSize.Height = (targetSize * sourceImage.Height) \ sourceImage.Width
            Else
                imageSize.Height = targetSize
                imageSize.Width = (targetSize * sourceImage.Width) \ sourceImage.Height
            End If
            Using image As New Bitmap(imageSize.Width, imageSize.Height)
                Using g As Graphics = Graphics.FromImage(image)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.DrawImage(sourceImage, New Rectangle(New Point(0, 0), imageSize))
                End Using
                Using stream As New IO.MemoryStream
                    image.Save(stream, imageFormat)
                    stream.Position = 0
                    stream.CopyTo(output)
                End Using
            End Using
        End Sub
    End Class  ' GdiEncoder

    Private Enum GdiCodec
        Bmp
        Jpeg
        Png
    End Enum  ' GdiCodec
End Class
