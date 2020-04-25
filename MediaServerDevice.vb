Imports System.Text
Imports System.Xml
Imports System.Runtime.InteropServices
Imports System.Reflection
Imports System.Threading
Imports System.Net
Imports System.Net.Sockets

Partial Public Class Plugin
    Private NotInheritable Class MediaServerDevice
        Inherits HQPDevice
        Private ReadOnly usedStreamHandle As New HashSet(Of Integer)
        Private Shared requestCounter As Integer = 0
        'Private bufferSize As Integer = 65536
        Private bufferSize As Integer = 409600

        Public Sub New(udn As Guid)
            MyBase.New(udn)

            server.HttpServer.AddRoute("HEAD", "/Files/*", New HttpRouteDelegate(AddressOf GetFile))
            server.HttpServer.AddRoute("GET", "/Files/*", New HttpRouteDelegate(AddressOf GetFile))
            server.HttpServer.AddRoute("HEAD", "/Encode/*", New HttpRouteDelegate(AddressOf GetEncodedFile))
            server.HttpServer.AddRoute("GET", "/Encode/*", New HttpRouteDelegate(AddressOf GetEncodedFile))
        End Sub

        Public Sub Dispose()
            [Stop]()
        End Sub

        Public ReadOnly Property HttpServer() As HttpServer
            Get
                Return server.HttpServer
            End Get
        End Property

        Public Overrides Sub Start()
            MyBase.Start()
        End Sub

        Private Sub GetFile(request As HttpRequest)

            Dim counter As Integer = Interlocked.Increment(requestCounter)
            Dim logId As String = "GetFile[" & counter & "]"

            'Debug
            'Dim index As Integer
            'For index = 0 To request.Headers.Keys.Count - 1
            'Dim key As String = request.Headers.Keys(index)
            'LogInformation(logId, "request header " & key & "=" & request.Headers(key))
            'Next

            Dim fileurl As String = Nothing
            Dim streamHandle As Integer = 0
            Dim dwFilename As String = ""
            fileurl = mbApiInterface.NowPlaying_GetFileUrl()
            Dim str As String = request.Url.Substring(request.Url.LastIndexOf("/"c) + 1)
            str = If(str.Length > 2, str.Split(CType(".", Char()))(0).Substring(2), "0")
            streamHandle = If(IsNumeric(str), CInt(str), 0)

            If (Settings.LogDebugInfo) Then
                LogInformation(logId, "Request url=" & request.Url)
                LogInformation(logId, "Nowplaying fileurl=" & fileurl)
            End If

            If streamHandle <> 0 Then
                Try
                    Bass.CloseStream(streamHandle)
                Catch ex As Exception
                End Try
            End If

            Dim ItemManager As ItemManager = New ItemManager()

            Dim musicBeePlayToMode As Boolean = True
            Dim mime As String = "application/octet-stream"

            Dim duration As TimeSpan
            If Not ItemManager.TryGetFileInfo(fileurl, duration) Then
                LogInformation(logId, "Bad id=" & request.Url)
                Throw New HttpException(404, "Bad parameter")
            End If
            Dim response As HttpResponse = request.Response
            If Not IO.File.Exists(fileurl) Then
                LogInformation(logId, "Not found file =" & fileurl)
                Throw New HttpException(404, "File not found")
            End If
            dwFilename = "mbplaying" + ItemManager.GetFileExt(fileurl)

            If Settings.LogDebugInfo Then
                Dim localAddress As String = "unknown address"
                Dim remoteAddress As String = "unknown address"
                If TypeOf request.Socket.Client.LocalEndPoint Is IPEndPoint Then
                    localAddress = DirectCast(request.Socket.Client.LocalEndPoint, IPEndPoint).Address.ToString()
                End If
                If TypeOf request.Socket.Client.RemoteEndPoint Is IPEndPoint Then
                    remoteAddress = DirectCast(request.Socket.Client.RemoteEndPoint, IPEndPoint).Address.ToString()
                End If
                LogInformation(logId & localAddress, request.Method & " " & fileurl & " to " & remoteAddress)
            End If
            Using stream As New IO.FileStream(fileurl, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read, buffersize, IO.FileOptions.SequentialScan)
                Dim fileLength As Long = stream.Length
                Dim range As String = Nothing
                If request.Headers.TryGetValue("range", range) Then
                    Dim values As String() = range.Split("="c).Last().Split("-"c).[Select](Function(a) a.Trim()).ToArray()
                    Dim byteRangeStart As Long = Long.Parse(values(0))
                    Dim byteRangeEnd As Long = byteRangeStart
                    If byteRangeStart < 0 Then
                        byteRangeStart += fileLength
                    End If
                    If values.Length < 2 OrElse Not Long.TryParse(values(1), byteRangeEnd) Then
                        byteRangeEnd = fileLength - 1
                    End If
                    If Settings.LogDebugInfo Then
                        LogInformation(logId, "Content-Range=" & String.Format("bytes {0}-{1}/{2}", byteRangeStart, byteRangeEnd, fileLength))
                    End If
                    response.AddHeader("Content-Range", String.Format("bytes {0}-{1}/{2}", byteRangeStart, byteRangeEnd, fileLength))
                    fileLength = byteRangeEnd - byteRangeStart + 1
                    response.StateCode = 206
                    stream.Position = byteRangeStart
                End If
                response.AddHeader(HttpHeader.Connection, "keep-alive")
                response.AddHeader(HttpHeader.KeepAlive, "timeout=20")
                response.AddHeader(HttpHeader.ContentLength, fileLength.ToString())
                response.AddHeader(HttpHeader.ContentType, mime)
                response.AddHeader(HttpHeader.AcceptRanges, "bytes")
                'response.AddHeader(HttpHeader.ContentDisposition, "attachment;filename=" & dwFilename)
                response.SendHeaders()
                If request.Method = "GET" Then
                    'Dim data(65535) As Byte
                    'Dim dataHandle As GCHandle
                    'dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned)
                    'Do
                    '    Dim count As Integer = stream.Read(data, 0, data.Length)
                    '    Debug.WriteLine(count)
                    '    If count <= 0 Then Exit Do
                    '    If send(request.Socket.Client.Handle, dataHandle.AddrOfPinnedObject, count, 0) = -1 Then
                    '        Debug.WriteLine("err=" & WSAGetLastError())
                    '        Exit Do
                    '    End If
                    '    Thread.Sleep(40)
                    'Loop
                    'Debug.WriteLine("done 1")
                    'shutdown(request.Socket.Client.Handle, 2)
                    'Debug.WriteLine("done 2")
                    'dataHandle.Free()
                    'Exit Sub
                    Dim startTime As Long
                    Dim errorCode As Integer
                    Dim playTime As Long
                    sendDataBarrier.Wait()
                    Try
                        startTime = DateTime.UtcNow.Ticks
                        errorCode = Sockets_Stream_File(stream.SafeFileHandle.DangerousGetHandle, CUInt(fileLength), request.Socket.Client.Handle)
                        playTime = (DateTime.UtcNow.Ticks - startTime) \ TimeSpan.TicksPerMillisecond
                    Finally
                        sendDataBarrier.Release()
                    End Try
                    If Settings.LogDebugInfo Then
                        LogInformation(logId, "exit=" & errorCode & ", playtime=" & playTime)
                    End If
                End If
            End Using
        End Sub
        '<DllImport("ws2_32.dll", CharSet:=CharSet.Unicode)> _
        'Private Shared Function send(socketHandle As IntPtr, data As IntPtr, length As Integer, flags As Integer) As Integer
        'End Function
        '<DllImport("ws2_32.dll", CharSet:=CharSet.Unicode)> _
        'Private Shared Function shutdown(socketHandle As IntPtr, flags As Integer) As Integer
        'End Function
        '<DllImport("ws2_32.dll", CharSet:=CharSet.Unicode)> _
        'Private Shared Function WSAGetLastError() As Integer
        'End Function

        Private Sub GetEncodedFile(request As HttpRequest)
            Dim counter As Integer = Interlocked.Increment(requestCounter)
            Dim logId As String = "GetEncodedFile[" & counter & "]"

            'debug
            'Dim index As Integer
            'For index = 0 To request.Headers.Keys.Count - 1
            '    Dim key As String = request.Headers.Keys(index)
            '    LogInformation(logId, "request header " & key & "=" & request.Headers(key))
            'Next

            Dim ItemManager As ItemManager = New ItemManager()
            Dim fileurl As String = Nothing
            fileurl = mbApiInterface.NowPlaying_GetFileUrl()
            Dim streamHandle As Integer = 0
            Dim cueIdx As String = ""

            Try
                fileurl = mbApiInterface.NowPlaying_GetFileUrl()
                Dim str As String = request.Url.Substring(request.Url.LastIndexOf("/"c) + 1)
                str = If(str.Length > 2, str.Split(CType(".", Char()))(0).Substring(2), "0")
                If (str.EndsWith("x")) Then
                    str = Left(str, str.Length - 1)
                    cueIdx = str.Substring(str.LastIndexOf("x") + 1)
                    str = Left(str, str.LastIndexOf("x"))
                    streamHandle = If(IsNumeric(str), CInt(str), 0)
                Else
                    streamHandle = If(IsNumeric(str), CInt(str), 0)
                End If
                If IsNumeric(cueIdx) Then
                    fileurl = fileurl & "#" & cueIdx & "#"
                End If
            Catch ex As Exception
                LogError(ex, logId, ex.StackTrace)
                cueIdx = ""
                streamHandle = 0
            End Try

            If (Settings.LogDebugInfo) Then
                LogInformation(logId, "Request url=" & request.Url)
                LogInformation(logId, "Nowplaying fileurl=" & fileurl)
                LogInformation(logId, "streamHandle=" & streamHandle)
            End If

            Dim targetMime As String = "audio/wav"
            Dim dwFilename As String = "mbplaying.wav"

            Dim encoder As AudioEncoder
            Select Case targetMime
                Case "audio/mpeg", "audio/mp3", "audio/x-mp3"
                    encoder = New AudioEncoder(FileCodec.Mp3)
                Case "audio/m4a", "audio/mp4", "audio/aac", "audio/x-aac"
                    encoder = New AudioEncoder(FileCodec.Aac)
                Case "audio/x-ogg", "audio/ogg"
                    encoder = New AudioEncoder(FileCodec.Ogg)
                Case "audio/x-ms-wma", "audio/wma", "audio/x-wma"
                    encoder = New AudioEncoder(FileCodec.Wma)
                Case "audio/wav", "audio/x-wav"
                    encoder = New AudioEncoder(FileCodec.Wave)
                Case Else
                    encoder = New AudioEncoder(FileCodec.Pcm)
            End Select

            Dim duration As TimeSpan
            If Not ItemManager.TryGetFileInfo(fileurl, duration) Then
                LogInformation(logId, "Invalid fileurl=" & fileurl)
                Throw New HttpException(404, "Bad parameter")
                Exit Sub
            End If

            'open a new stream instead
            If streamHandle <> 0 Then
                Try
                    Bass.CloseStream(streamHandle)
                Catch ex As Exception
                End Try
            End If

            'streamHandle = mbApiInterface.Player_OpenStreamHandle(fileurl, True, Settings.ServerEnableSoundEffects, Settings.ServerReplayGainMode)
            streamHandle = mbApiInterface.Player_OpenStreamHandle(fileurl, False, Settings.ServerEnableSoundEffects, Settings.ServerReplayGainMode)

            If streamHandle = 0 Then
                LogInformation(logId, "Stream zero=" & request.Url)
                Throw New HttpException(404, "File not found")
                Exit Sub
            End If

            Dim response As HttpResponse = request.Response
            Dim fileDuration As Double = 0
            Dim fileDecodeStartPos As Long = 0
            Dim fileEncodeLength As Long = 0
            Dim isPartialContent As Boolean = False
            Dim isPcmData As Boolean = (encoder.Codec = FileCodec.Pcm OrElse encoder.Codec = FileCodec.Wave)
            Dim sampleRate As Integer
            Dim channelCount As Integer
            Dim streamCodec As FileCodec
            Dim bitDepth As Integer

            Bass.TryGetStreamInformation(streamHandle, sampleRate, channelCount, streamCodec)
            If duration.Ticks <= 0 Then
                fileDuration = Bass.GetDecodedDuration(streamHandle)
                If fileDuration > 0 Then
                    duration = New TimeSpan(CLng(fileDuration * TimeSpan.TicksPerSecond))
                End If
            Else
                fileDuration = duration.Ticks / TimeSpan.TicksPerSecond
            End If

            bitDepth = If(Not isPcmData, 16, Settings.BitDepth)

            Dim sourceStreamHande As Integer = streamHandle
            Dim streamStartPosition As Long = Bass.GetStreamPosition(sourceStreamHande)
            'streamHandle = encoder.GetEncodeStreamHandle(sourceStreamHande, sampleRate, channelCount, bitDepth, True)
            streamHandle = encoder.GetEncodeStreamHandle(sourceStreamHande, sampleRate, channelCount, bitDepth, False)

            Dim decodedLength As Long = Bass.GetDecodedLength(streamHandle, fileDuration)
            If bitDepth <> 24 Then
                fileEncodeLength = decodedLength \ 2
            Else
                fileEncodeLength = (decodedLength * 3) \ 4
            End If

            Dim contentLength As Long = fileEncodeLength
            If encoder.Codec = FileCodec.Wave Then
                contentLength += 44
            End If

            Dim byteRange As String = Nothing
            If request.Headers.TryGetValue("range", byteRange) Then
                Dim values As String() = byteRange.Split("="c).Last().Split("-"c).[Select](Function(a) a.Trim()).ToArray()
                Dim byteRangeStart As Long
                Dim byteRangeEnd As Long
                If Not Long.TryParse(values(0), byteRangeStart) Then
                    byteRangeStart = 0
                ElseIf byteRangeStart < 0 Then
                    byteRangeStart += contentLength
                End If
                If values.Length < 2 OrElse Not Long.TryParse(values(1), byteRangeEnd) Then
                    byteRangeEnd = contentLength - 1
                End If
                response.StateCode = 206
                response.AddHeader("Content-Range", String.Format("bytes {0}-{1}/{2}", byteRangeStart, byteRangeEnd, contentLength))
                If Settings.LogDebugInfo Then
                    LogInformation(logId, "Content-Range=" & String.Format("bytes {0}-{1}/{2}", byteRangeStart, byteRangeEnd, contentLength))
                End If
                isPartialContent = (byteRangeStart > 0)
                contentLength = byteRangeEnd - byteRangeStart + 1
                fileEncodeLength = contentLength
                If encoder.Codec = FileCodec.Wave AndAlso byteRangeStart > 0 Then
                    byteRangeStart -= 44
                End If
                If bitDepth <> 24 Then
                    fileDecodeStartPos = byteRangeStart * 2
                Else
                    fileDecodeStartPos = (byteRangeStart * 4) \ 3
                End If
            End If

            response.AddHeader(HttpHeader.Connection, "keep-alive")
            response.AddHeader(HttpHeader.KeepAlive, "timeout=20")
            response.AddHeader(HttpHeader.AcceptRanges, "bytes")
            response.AddHeader(HttpHeader.ContentType, targetMime)
            response.AddHeader(HttpHeader.ContentLength, contentLength.ToString())
            'response.AddHeader(HttpHeader.ContentLength, "4294967294")
            'response.AddHeader(HttpHeader.ContentType, "application/octet-stream")
            'response.AddHeader(HttpHeader.ContentDisposition, "attachment;filename=" & dwFilename)
            response.SendHeaders()

            If Settings.LogDebugInfo Then
                Dim localAddress As String = "unknown address"
                Dim remoteAddress As String = "unknown address"
                If TypeOf request.Socket.Client.LocalEndPoint Is IPEndPoint Then
                    localAddress = DirectCast(request.Socket.Client.LocalEndPoint, IPEndPoint).Address.ToString()
                End If
                If TypeOf request.Socket.Client.RemoteEndPoint Is IPEndPoint Then
                    remoteAddress = DirectCast(request.Socket.Client.RemoteEndPoint, IPEndPoint).Address.ToString()
                End If
                LogInformation(logId & " " & localAddress, request.Method & " " & fileurl & " to " & remoteAddress & "; mime=" & targetMime & ",rate=" & sampleRate & ",channels=" & channelCount)
            End If

            'LogInformation(logId, "streamHandle=" & streamHandle)
            'LogInformation(logId, "sourceStreamHande=" & sourceStreamHande)
            'LogInformation(logId, "fileDecodeStartPos=" & fileDecodeStartPos)
            'LogInformation(logId, "streamStartPosition=" & streamStartPosition)
            'LogInformation(logId, "isPartialContent=" & isPartialContent)
            'LogInformation(logId, "fileEncodeLength=" & fileEncodeLength)
            'LogInformation(logId, "isPartialContent=" & isPartialContent)
            'LogInformation(logId, "bitDepth=" & bitDepth)
            'LogInformation(logId, "contentLength=" & contentLength)

            'debug
            'Dim seconds As Double = 180
            'fileDecodeStartPos = Bass.GetDecodedLength(streamHandle, seconds)

            If String.Compare(request.Method, "GET", StringComparison.OrdinalIgnoreCase) = 0 Then
                If fileDecodeStartPos > 0 Then
                    'Bass.SetEncodeStreamPosition(sourceStreamHande, streamStartPosition + fileDecodeStartPos)
                    Bass.SetEncodeStreamPosition(streamHandle, streamStartPosition + fileDecodeStartPos)
                End If
                encoder.StartEncode(fileurl, streamHandle, isPartialContent, fileEncodeLength, bitDepth, request.Socket.Client.Handle, logId)
                Bass.CloseStream(sourceStreamHande)
            End If

        End Sub


        <DllImport("MusicBeeBass.dll", CallingConvention:=CallingConvention.Cdecl)> _
        Private Shared Function Sockets_Stream_File(fileHandle As IntPtr, limit As UInteger, socketHandle As IntPtr) As Integer
        End Function
    End Class  ' MediaServerDevice
End Class