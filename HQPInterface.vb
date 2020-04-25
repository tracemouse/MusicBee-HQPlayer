Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Xml

Partial Public Class Plugin
    Public Enum HQP_PlayState
        Undefined = 4
        Loading = 3
        Playing = 2
        Paused = 1
        Stopped = 0
    End Enum

    Public Enum HQP_Cmd
        undefined = 0
        GetInfo
        State
        Status
        Volume
        VolumeRange
        VolumeUp
        VolumeDown
        VolumeMute
        LibraryGet
        LibraryDirectory
        LibraryFile
        LibraryPicture
        LibraryLoad
        PlaylistAdd
        PlaylistRemove
        PlaylistMoveUp
        PlaylistMoveDown
        PlaylistClear
        PlaylistGet
        PlaylistItem
        MatrixListProfiles
        MatrixGetProfile
        MatrixSetProfile
        SelectTrack
        Play
        Pause
        [Stop]
        Previous
        [Next]
        Backward
        Forward
        Seek
        SetMode
        GetModes
        ModesItem
        SetFilter
        GetFilters
        SetShaping
        GetShapers
        SetRate
        GetRates
        SetInvert
        SetRepeat
        SetRandom
        SetDisplay
        GetDisplay
        SetTransport
        SetTransportPath
        GetTransport
        GetInputs
        metadata
    End Enum

    Private Class HQPInterface

        Public Delegate Sub DelegateHqpLister(Packet As Byte())

        Dim MyCallback As DelegateHqpLister = Nothing

        Public profile As HQPProfile = Nothing

        Private client As TcpClient = Nothing
        Private connected As Boolean = False
        Public listenThead As Thread = Nothing

        Public Sub New(profile As HQPProfile)
            Me.profile = profile
        End Sub

        Public Sub RegisterCallback(callback As DelegateHqpLister)
            MyCallback = callback
        End Sub

        Public Function Connect() As Boolean

            client = New TcpClient()
            client.ReceiveBufferSize = 4095
            client.ReceiveTimeout = profile.receiveTimeout
            client.SendTimeout = profile.sendTimeout

            Try
                Dim ip As String = profile.IpAddress
                ip = ip.Replace("localhost", "127.0.0.1")
                'client.Connect(IPAddress.Parse(ip), profile.Port)
                Dim ar As IAsyncResult = client.BeginConnect(IPAddress.Parse(ip), profile.Port, Nothing, Nothing)
                Dim success As Boolean = ar.AsyncWaitHandle.WaitOne(profile.connectTimeout)
                If (Not success) Then
                    Throw New Exception("Socket connection timeout")
                    Return False
                End If
                If client.Connected Then
                    profile.IsAlive = True
                    connected = True
                    listenThead = New Thread(New ParameterizedThreadStart(AddressOf DataListener))
                    listenThead.IsBackground = True
                    listenThead.Start(client)
                End If
                Return True
            Catch ex As Exception
                'MsgBox(ex.Message)
                LogError(ex, "Close", ex.StackTrace)
                Connect = False
                Throw ex
            End Try
            Return False
        End Function

        Public Sub Close()
            Try
                If (Not (listenThead Is Nothing)) Then
                    If listenThead.IsAlive Then
                        listenThead.Abort()
                    End If
                End If

                If Not (client Is Nothing) Then
                    If client.Connected Then
                        client.Close()
                    End If
                End If
                connected = False
                profile.IsAlive = False
            Catch ex As Exception
                LogError(ex, "Close", ex.StackTrace)
            End Try
        End Sub

        Public Sub Dispose()
            Close()
        End Sub


        Public Function Send(msg As String, Optional ByVal sync As Boolean = False) As String
            Try
                If (sync) Then
                    Return SendSync(msg)
                End If

                If (Settings.LogDebugSocketMsg) Then
                    LogInformation("Send", "send msg =" & msg)
                End If

                Dim bytes() As Byte = Encoding.UTF8.GetBytes(msg)
                If client.Connected Then
                    client.Client.Send(bytes)
                    Return "OK"
                Else
                    'MsgBox("HQPlayer is not alive.")
                    Throw New Exception("Socket connection is not alive.")
                End If
            Catch ex As Exception
                'Connect()
                'Dim bytes() As Byte = Encoding.UTF8.GetBytes(msg)
                'If client.Connected Then
                ' client.Client.Send(bytes)
                ' Else
                'MsgBox("HQPlayer is not alive.")
                'End If
                LogError(ex, "Send", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function SendSync(msg As String) As String
            SendSync = Nothing

            If (Settings.LogDebugSocketMsg) Then
                LogInformation("SendSync", "send msg =" & msg)
            End If

            Dim maxWait As Integer = 3000    '3 seconds
            Dim sClient As TcpClient = New TcpClient()
            sClient.ReceiveBufferSize = 4095
            sClient.ReceiveTimeout = profile.receiveTimeout
            sClient.SendTimeout = profile.sendTimeout

            Try
                Dim ip As String = profile.IpAddress
                ip = ip.Replace("localhost", "127.0.0.1")

                'sClient.Connect(IPAddress.Parse(ip), profile.Port)
                Dim ar As IAsyncResult = sClient.BeginConnect(IPAddress.Parse(ip), profile.Port, Nothing, Nothing)
                Dim success As Boolean = ar.AsyncWaitHandle.WaitOne(profile.connectTimeout)
                If (Not success) Then
                    Throw New Exception("Socket connection timeout")
                    Return Nothing
                End If

                If sClient.Connected Then
                    Dim bytes() As Byte = Encoding.UTF8.GetBytes(msg)
                    sClient.Client.Send(bytes)
                    Dim Buffer(4095) As Byte
                    Dim RecLength As Integer
                    Try
                        Dim startTime As Date = Now
                        Dim count As Integer = 0

                        While (count <= maxWait)
                            RecLength = sClient.Client.Receive(Buffer)
                            Select Case RecLength
                                Case 0
                                    Exit Try '对端断开，跳出流程
                                Case Else
                                    '正常处理逻辑
                                    Dim Packet As Byte() = New Byte(RecLength - 1) {}
                                    Array.Copy(Buffer, 0, Packet, 0, Packet.Length) '获取实际数据
                                    Dim hqpMsg As String = Encoding.UTF8.GetString(Packet)
                                    If (Settings.LogDebugSocketMsg) Then
                                        LogInformation("SendSync", "received msg =" & hqpMsg)
                                    End If
                                    SendSync = hqpMsg
                                    Try
                                        sClient.Client.Close()
                                        sClient = Nothing
                                    Catch ex As Exception
                                    End Try
                                    Exit Function
                            End Select
                            count = CInt(DateDiff(DateInterval.Second, startTime, Now))
                        End While
                    Catch ex As SocketException
                        Select Case ex.NativeErrorCode
                            Case 10053, 10054
                                '远端断开
                            Case Else
                                '其他错误
                        End Select
                        LogError(ex, "SendSync", ex.StackTrace)
                        Throw ex
                    Finally
                        sClient = Nothing
                    End Try
                End If
            Catch ex As Exception
                LogError(ex, "SendSync", ex.StackTrace)
                Throw ex
            Finally
                sClient = Nothing
            End Try
        End Function

        Public Sub DataListener(obj As Object)
            Dim client As TcpClient = DirectCast(obj, TcpClient)
            'MsgBox(client.Client.SocketType)

            Dim Buffer(4095) As Byte
            Dim RecLength As Integer
            Try
                Do Until Not client.Connected
                    RecLength = client.Client.Receive(Buffer)
                    Select Case RecLength
                        Case 0
                            Exit Try '对端断开，跳出流程
                        Case Else
                            '正常处理逻辑
                            Dim Packet As Byte() = New Byte(RecLength - 1) {}
                            Array.Copy(Buffer, 0, Packet, 0, Packet.Length) '获取实际数据
                            MyCallback.Invoke(Packet)
                    End Select
                Loop
            Catch ex As SocketException
                Select Case ex.NativeErrorCode
                    Case 10053, 10054
                        '远端断开
                    Case Else
                        '其他错误
                End Select
            End Try

        End Sub

        Public Function HQP_CheckAlive() As String
            profile.IsAlive = False
            Try
                Dim hqpMsg As String = HQP_GetInfo(True)
                profile.IsAlive = True
                Return hqpMsg
            Catch ex As Exception
                LogError(ex, "HQP_GetInfo", ex.StackTrace)
                'Throw ex
            End Try
            Return Nothing

        End Function

        Public Function HQP_GetInfo(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("GetInfo")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_GetInfo", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Play(lastTrack As Boolean, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("Play", "")
                    writer.WriteAttributeString("last", CStr(Bool2Int(lastTrack)))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Play", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Pause(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Pause")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Pause", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Stop(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Stop")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Stop", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Previous(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Previous")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Previous", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Next(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Next")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Next", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Backward(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Backward")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Backward", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Forward(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("Forward")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Forward", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_VolumeUp(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("VolumeUp")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_VolumeUp", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_VolumeDown(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("VolumeDown")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_VolumeDown", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_VolumeMute(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("VolumeMute")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_VolumeMute", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_VolumeRange(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("VolumeRange")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_VolumeRange", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Volume(volume As Double, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("Volume", "")
                    writer.WriteAttributeString("value", CStr(volume))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Volume", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Seek(position As Integer, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("Seek", "")
                    writer.WriteAttributeString("position", CStr(position))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Seek", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_State(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("State")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_State", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_Status(subscribe As Boolean, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("Status", "")
                    writer.WriteAttributeString("subscribe", CStr(Bool2Int(subscribe)))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Seek", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_SetRepeat(value As Integer, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("SetRepeat", "")
                    writer.WriteAttributeString("value", CStr(value))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Seek", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_SetRandom(value As Boolean, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("SetRandom", "")
                    writer.WriteAttributeString("value", CStr(Bool2Int(value)))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Seek", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_PlaylistAdd(uri As String, queued As Boolean, clear As Boolean, Optional ByVal sync As Boolean = False) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement("PlaylistAdd", "")
                    writer.WriteAttributeString("uri", uri)
                    writer.WriteAttributeString("queued", CStr(Bool2Int(queued)))
                    writer.WriteAttributeString("clear", CStr(Bool2Int(clear)))
                    writer.WriteEndElement()
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return Send(text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_Seek", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function HQP_PlaylistClear(Optional ByVal sync As Boolean = False) As String
            Try
                Dim Text As String = GetActionXML("PlaylistClear")
                Return Send(Text, sync)
            Catch ex As Exception
                LogError(ex, "HQP_PlaylistClear", ex.StackTrace)
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function Bool2Int(value As Boolean) As Integer
            If (value) Then
                Bool2Int = 1
            Else
                Bool2Int = 0
            End If
        End Function

        Public Function GetActionXML(action As String) As String
            Try
                Dim xmlSettings As New XmlWriterSettings
                xmlSettings.Indent = False
                xmlSettings.Encoding = New UTF8Encoding(False)
                Dim text As String
                Dim buf As New IO.MemoryStream
                Using writer As New XmlTextWriter(buf, New UTF8Encoding(False))
                    writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    writer.WriteStartElement(action, "")
                End Using
                Dim b() As Byte = buf.ToArray()
                text = Encoding.UTF8.GetString(b, 0, b.Length)
                Return text
            Catch ex As Exception
                LogError(ex, "GetActionXML", ex.StackTrace)
            End Try
            Return ""
        End Function

        Public Function ParseXML(msg As String) As HQPProfile
            Try
                Return ParseXML(Encoding.UTF8.GetBytes(msg))
            Catch ex As Exception
                Throw ex
            End Try
            Return Nothing
        End Function

        Public Function ParseXML(Packet As Byte()) As HQPProfile
            Try
                Dim Stream As New MemoryStream(Packet)
                Using reader As XmlReader = XmlReader.Create(Stream)
                    While reader.Read()
                        If reader.IsStartElement() Then
                            Dim cmd As String = reader.Name
                            profile.cmd = DirectCast([Enum].Parse(GetType(HQP_Cmd), cmd), HQP_Cmd)

                            Select Case profile.cmd
                                Case HQP_Cmd.GetInfo
                                    profile.product = reader.GetAttribute("product").ToString()
                                    profile.version = reader.GetAttribute("version").ToString()
                                    profile.platform = reader.GetAttribute("platform").ToString()
                                Case HQP_Cmd.State
                                    profile.state = CInt(reader.GetAttribute("state").ToString())
                                    profile.mode = CInt(reader.GetAttribute("mode").ToString())
                                    profile.filter = CInt(reader.GetAttribute("filter").ToString())
                                    profile.shaper = CInt(reader.GetAttribute("shaper").ToString())
                                    profile.rate = CInt(reader.GetAttribute("rate").ToString())
                                    profile.volume = CDbl(reader.GetAttribute("volume").ToString())
                                    profile.active_mode = CInt(reader.GetAttribute("active_mode").ToString())
                                    profile.active_rate = CInt(reader.GetAttribute("active_rate").ToString())
                                    profile.invert = CInt(reader.GetAttribute("invert").ToString())
                                    profile.convolution = CInt(reader.GetAttribute("convolution").ToString())
                                    profile.repeat = CInt(reader.GetAttribute("repeat").ToString())
                                    profile.random = CInt(reader.GetAttribute("random").ToString())
                                Case HQP_Cmd.Status
                                    profile.state = CInt(reader.GetAttribute("state").ToString())
                                    profile.track = CInt(reader.GetAttribute("track").ToString())
                                    profile.min = CInt(reader.GetAttribute("min").ToString())
                                    profile.sec = CInt(reader.GetAttribute("sec").ToString())
                                    profile.volume = CDbl(reader.GetAttribute("volume").ToString())
                                    profile.tracks_total = CInt(reader.GetAttribute("tracks_total").ToString())
                                    profile.queued = CInt(reader.GetAttribute("queued").ToString())
                                    profile.begin_min = CInt(reader.GetAttribute("begin_min").ToString())
                                    profile.begin_sec = CInt(reader.GetAttribute("begin_sec").ToString())
                                    profile.total_min = CInt(reader.GetAttribute("total_min").ToString())
                                    profile.total_sec = CInt(reader.GetAttribute("total_sec").ToString())

                                    'HQPProfile.active_mode_str = reader.GetAttribute("active_mode_str").ToString()
                                    'HQPProfile.active_filter_str = reader.GetAttribute("active_filter_str").ToString()
                                    'HQPProfile.active_shaper_str = reader.GetAttribute("active_shaper_str").ToString()
                                    'HQPProfile.active_rate = CInt(reader.GetAttribute("active_rate").ToString())
                                    'HQPProfile.active_bits = CInt(reader.GetAttribute("active_bits").ToString())
                                    'HQPProfile.active_channels = CInt(reader.GetAttribute("active_channels").ToString())
                                Case HQP_Cmd.VolumeRange
                                    profile.VolumeMin = CDbl(reader.GetAttribute("min").ToString())
                                    profile.VolumeMax = CDbl(reader.GetAttribute("max").ToString())
                                    Dim enabled As Integer = CInt(reader.GetAttribute("enabled").ToString())
                                    If (enabled = 0) Then
                                        profile.VolumeEnabled = False
                                    Else
                                        profile.VolumeEnabled = True
                                    End If
                                Case Else
                                    Dim result As String = reader.GetAttribute("result")
                                    If (result = "OK") Then
                                        profile.result = True
                                    Else
                                        profile.result = False
                                    End If
                            End Select
                        End If
                    End While
                End Using
                Return profile
            Catch ex As Exception
                LogError(ex, "ParseXML", ex.StackTrace)
            End Try
            Return Nothing
        End Function

        Public Function MbVolume2HqpVolme(volume As UInteger) As Double
            Try
                Dim min As Double = profile.VolumeMin
                Dim max As Double = profile.VolumeMax
                Dim hqpVolume As Double = 0
                If min < 0 Then
                    hqpVolume = CDbl(volume) + min
                Else
                    hqpVolume = CDbl(volume) - min
                End If
                'LogInformation("MbVolume2HqpVolme", "min=" & min & ",max=" & max & ",mbVolume=" & volume & ",hqpVolume=" & CStr(hqpVolume))
                Return hqpVolume
            Catch ex As Exception
                LogError(ex, "MbVolume2HqpVolme", ex.StackTrace)
                Throw ex
            End Try
            Return 0
        End Function

        Public Function HqpVolume2MbVolme(volume As Double) As UInteger
            Try
                Dim min As Double = profile.VolumeMin
                Dim max As Double = profile.VolumeMax
                Dim mbVolume As UInteger = CUInt(volume - min)

                'LogInformation("MbVolume2HqpVolme", "min=" & min & ",max=" & max & ",mbVolume=" & CStr(mbVolume) & ",hqpVolume=" & CStr(volume))
                Return mbVolume
            Catch ex As Exception
                LogError(ex, "HqpVolume2MbVolme", ex.StackTrace)
                Throw ex
            End Try
            Return 0
        End Function

    End Class  'HQPInterface

End Class
