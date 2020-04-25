Imports System.Text
Imports System.IO
Imports System.Threading
Imports System.Xml
Imports System.Runtime.InteropServices
Imports System.Net
Imports System.Net.Sockets

Partial Public Class Plugin

    Private Class MediaRendererDevice
        Public hqpInterface As HQPInterface = Nothing
        Public hqpProfile As HQPProfile = Nothing
        Public isEncodeStream As Boolean = False

        Public ReadOnly FriendlyName As String
        Public ReadOnly Udn As New HashSet(Of String)(StringComparer.Ordinal)
        Private isActive As Boolean = False
        Private ReadOnly modelDescription As String = ""
        Private ReadOnly supportSetNextInQueue As Boolean = False
        'Private supportPlayPause As Boolean = False
        'Private supportPlayNext As Boolean = False
        'Private supportPlayPrev As Boolean = False
        'Private supportPlaySeek As Boolean = False
        ''Private avTransportEventSid As String
        ''Private avTransportEventTimeout As Integer
        ''Private renderingControlEventSid As String
        ''Private renderingControlEventTimeout As Integer
        Private deviceMinVolume As UInteger = 0
        Private deviceMaxVolume As UInteger = 100
        Private currentVolume As UInteger
        Private currentMute As Boolean
        Private currentErrorCount As Integer = 0
        Private currentPlayState As PlayState = PlayState.Undefined
        Private currentPlaySpeed As Integer = 1
        'Private currentTransportStatus As String
        Private currentPlayStartTimeEstimated As Boolean
        Private currentPlayStartTicks As Long
        Private currentTrackDurationTicks As Long
        Private currentPlayPositionMs As Integer
        'Private currentPlayUrl As String
        'Private nextPlayUrl As String
        Private lastUserInitiatedStop As Long = 0
        Private queueNextTrackPending As Boolean = False
        'Private queueNextFailedCount As Integer = 0
        Private lastServerHeader As String

        Private ReadOnly resourceLock As New Object
        Private playToMode As Boolean = False
        Private statusTimerInterval As Integer = 500

        Private ReadOnly hqpStatusTimer As New Timer(AddressOf OnHqpStatusCheck, Nothing, Timeout.Infinite, Timeout.Infinite)

        Public Sub New(profile As HQPProfile)
            'Me.deviceLocationUrl = deviceLocationUrl
            Me.hqpProfile = profile
            hqpInterface = New HQPInterface(profile)
            hqpInterface.RegisterCallback(New HQPInterface.DelegateHqpLister(AddressOf OnHQPlayerCallback))

            If Settings.LogDebugInfo Then
                LogInformation("MediaRendererDevice", "HQPlayer render=" & profile.ProfileName + "," + profile.IpAddress)
            End If

            FriendlyName = profile.ProfileName
            modelDescription = profile.ProfileName
            Udn.Add(profile.udn.ToString)
        End Sub

        Public Sub Dispose()
            If isActive Then
                StopPlayback()
                If mbApiInterface.Player_GetPlayState() <> Plugin.PlayState.Stopped Then
                    mbApiInterface.Player_Stop()
                End If
                Activate(False)
            End If

            hqpStatusTimer.Dispose()

            If Not IsNothing(hqpInterface) Then
                hqpInterface.Dispose()
            End If

        End Sub

        Public ReadOnly Property IsValidRenderer() As Boolean
            Get
                hqpInterface.HQP_CheckAlive()
                hqpProfile = hqpInterface.profile
                Return hqpProfile.IsAlive
            End Get
        End Property

        Public Function Activate(active As Boolean) As Boolean
            Try
                If active Then
                    If isActive Then
                        Activate(False)
                    End If

                    hqpInterface.Connect()

                    hqpInterface.HQP_CheckAlive()
                    hqpProfile = hqpInterface.profile
                    If Not hqpProfile.IsAlive Then
                        MsgBox("HQPlayer is not alive, please make sure HQPlayer is running on " + hqpProfile.IpAddress + ".")
                        Return False
                    End If

                    'sync volume range
                    Dim hqpMsg As String = hqpInterface.HQP_VolumeRange(True)
                    hqpProfile = hqpInterface.ParseXML(hqpMsg)
                    deviceMinVolume = 0
                    deviceMaxVolume = CUInt(hqpProfile.VolumeMax - hqpProfile.VolumeMin)

                    StopPlayback()
                    GetPlayStateInformation()
                    isActive = True
                    Return True
                ElseIf isActive Then
                    isActive = False
                    playToMode = False
                    hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
                    If currentPlayState <> PlayState.Stopped Then
                        currentPlayState = PlayState.Stopped
                        mbApiInterface.Player_Stop()
                    End If
                    hqpInterface.Dispose()
                End If
                Return True
            Catch ex As Exception
                LogError(ex, "Activate:" & active)
                Return False
            End Try
        End Function

        Public Sub OnHQPlayerCallback(Packet As Byte())
            Try
                'hqpProfile = hqpInterface.ParseXML(Packet)
                'Select Case True
                'Case hqpProfile.cmd_GetInfo
                '
                'End Select
            Catch ex As Exception
                LogError(ex, "OnHQPlayerCallback", ex.StackTrace)
            End Try
        End Sub

        Private Function ProcessNewPlayState(oldPlayState As PlayState) As Boolean
            If Not playToMode Then
                Return False
            End If

            Select Case currentPlayState
                Case PlayState.Stopped
                    hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
                    currentPlayStartTimeEstimated = True
                    Dim stopInvokedByHQP As Boolean = False
                    'stopInvokedByHQP = ((DateTime.UtcNow.Ticks - lastUserInitiatedStop) < 2000 * TimeSpan.TicksPerMillisecond)
                    'stopInvokedByHQP = ((DateTime.UtcNow.Ticks - currentPlayStartTicks) < currentTrackDurationTicks)
                    currentPlayPositionMs = 0
                    lastUserInitiatedStop = 0
                    queueNextTrackPending = False
                    If (oldPlayState = PlayState.Playing OrElse oldPlayState = PlayState.Loading) AndAlso Not supportSetNextInQueue AndAlso Not stopInvokedByHQP Then
                        currentPlayStartTicks = Long.MaxValue
                        If mbApiInterface.Player_PlayNextTrack() Then
                            Return False
                        End If
                    End If
                    Return True
                Case PlayState.Playing
                    If currentPlayStartTimeEstimated Then
                        currentPlayPositionMs = 0
                        currentPlayStartTicks = DateTime.UtcNow.Ticks
                        currentPlayStartTimeEstimated = False
                    Else
                        currentPlayStartTicks = DateTime.UtcNow.Ticks - currentPlayPositionMs * TimeSpan.TicksPerMillisecond
                    End If
                    hqpStatusTimer.Change(0, statusTimerInterval)
                    Return True
                Case PlayState.Paused
                    currentPlayPositionMs = CInt((DateTime.UtcNow.Ticks - currentPlayStartTicks) \ TimeSpan.TicksPerMillisecond)
                    hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
                    Return True
            End Select
            Return False
        End Function

        Private Sub SyncNewPlayState()
            Dim mbPlayState As PlayState = mbApiInterface.Player_GetPlayState()
            If currentPlayState <> mbPlayState Then
                If Settings.LogDebugInfo Then
                    LogInformation("SyncNewPlayState", currentPlayState.ToString & ",mb=" & mbPlayState.ToString())
                End If
                Select Case currentPlayState
                    Case PlayState.Stopped
                        If mbPlayState <> PlayState.Loading Then
                            mbApiInterface.Player_Stop()
                        End If
                    Case PlayState.Paused
                        If mbPlayState = PlayState.Playing Then
                            mbApiInterface.Player_PlayPause()
                        End If
                    Case PlayState.Playing
                        If mbPlayState = PlayState.Paused Then
                            mbApiInterface.Player_PlayPause()
                        End If
                End Select
            End If
        End Sub

        Public ReadOnly Property PlayState() As PlayState
            Get
                Return currentPlayState
            End Get
        End Property

        Public ReadOnly Property PlayPositionMs() As Integer
            Get
                Select Case currentPlayState
                    Case PlayState.Playing, Plugin.PlayState.Loading
                        If currentPlayStartTimeEstimated Then
                            Return currentPlayPositionMs
                        Else
                            Return CInt((DateTime.UtcNow.Ticks - currentPlayStartTicks) \ TimeSpan.TicksPerMillisecond)
                        End If
                    Case PlayState.Paused
                        Return currentPlayPositionMs
                End Select
                Return 0
            End Get
        End Property

        Private Function GetPlayStateInformation() As Exception
            Try
                Dim hqpMsg As String = hqpInterface.HQP_Status(True, True)
                hqpProfile = hqpInterface.ParseXML(hqpMsg)

                Select Case hqpProfile.state
                    Case HQP_PlayState.Stopped
                        currentPlayState = PlayState.Stopped
                    Case HQP_PlayState.Playing
                        currentPlayState = PlayState.Playing
                    Case HQP_PlayState.Paused
                        currentPlayState = PlayState.Paused
                    Case HQP_PlayState.Loading
                        currentPlayState = PlayState.Loading
                End Select

                'sync volume
                currentVolume = hqpInterface.HqpVolume2MbVolme(hqpProfile.volume)
                mbApiInterface.Player_SetVolume(CSng(currentVolume / deviceMaxVolume))

                'sync timer bar
                If currentPlayStartTimeEstimated Then
                    Dim value As TimeSpan
                    Dim hhmmss As String = "00:" & hqpProfile.min & ":" & hqpProfile.sec
                    If Not TimeSpan.TryParse(hhmmss, value) Then
                        currentPlayPositionMs = 0
                    Else
                        currentPlayPositionMs = CInt(value.Ticks \ TimeSpan.TicksPerMillisecond)
                    End If
                End If

                Return Nothing
            Catch ex As XmlException
                Return LogError(ex, "GetHqpState")
            Catch ex As Exception
                Return ex
            End Try
        End Function

        Public Sub SetVolume(value As Single)
            Try
                Dim newVolume As UInteger = CUInt(value * deviceMaxVolume)
                If newVolume <> currentVolume Then
                    Dim hqpMsg As String = hqpInterface.HQP_Volume(hqpInterface.MbVolume2HqpVolme(newVolume))
                    currentVolume = newVolume
                End If
            Catch ex As Exception
                LogError(ex, "SetVolume")
            End Try
        End Sub

        Public Sub SetMute(value As Boolean)
            If value <> currentMute Then
                Try
                    Dim volume As UInteger = deviceMaxVolume
                    If (value) Then
                        volume = deviceMinVolume
                    Else
                        volume = CUInt(mbApiInterface.Player_GetVolume() * deviceMaxVolume)
                    End If
                    Dim hqpMsg As String = hqpInterface.HQP_Volume(hqpInterface.MbVolume2HqpVolme(volume))
                    currentMute = value
                Catch ex As Exception
                    LogError(ex, "SetMute")
                End Try
            End If
        End Sub

        Public Function PlayToDevice(url As String, streamHandle As Integer) As Boolean
            Try
                If Settings.LogDebugInfo Then
                    LogInformation("PlayToDevice", "url=" & url & ",streamHandle=" & streamHandle)
                End If
                hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
                Dim sourceUrl As String
                Dim ext As String
                Dim hqpUrl As String
                Dim cueIdx As String = ""

                If String.IsNullOrEmpty(url) OrElse url.EndsWith("#") Then
                    sourceUrl = "stream"
                    'currentTrackDurationTicks = Long.MaxValue
                    Try
                        cueIdx = Left(url, url.Length - 1)
                        cueIdx = cueIdx.Substring(cueIdx.LastIndexOf("#") + 1)
                    Catch ex As Exception
                        LogError(ex, "PlayToDevice", ex.StackTrace)
                        cueIdx = ""
                    End Try
                Else
                    sourceUrl = url
                    ext = Path.GetExtension(sourceUrl)
                    If Not HQPProfile.SupportedExt.Contains(ext.ToUpper) Then
                        sourceUrl = "stream"
                        'currentTrackDurationTicks = Long.MaxValue
                    Else
                        'Long.TryParse(mbApiInterface.Library_GetFileProperty(url, DirectCast(-FilePropertyType.Duration, FilePropertyType)), currentTrackDurationTicks)
                    End If
                End If

                If String.IsNullOrEmpty(url) Then
                    currentTrackDurationTicks = Long.MaxValue
                Else
                    Long.TryParse(mbApiInterface.Library_GetFileProperty(url, DirectCast(-FilePropertyType.Duration, FilePropertyType)), currentTrackDurationTicks)
                End If

                currentPlayStartTimeEstimated = True
                playToMode = True

                If (sourceUrl = "stream") Then
                    isEncodeStream = True
                    If IsNumeric(cueIdx) Then
                        hqpUrl = PrimaryHostUrl() & "/Encode/mb" & streamHandle & "x" & cueIdx & "x.wav"
                    Else
                        hqpUrl = PrimaryHostUrl() & "/Encode/mb" & streamHandle & ".wav"
                    End If
                Else
                    isEncodeStream = False
                    hqpUrl = PrimaryHostUrl() & "/Files/mb" & streamHandle & ext
                End If

                'isEncodeStream = True
                'hqpUrl = PrimaryHostUrl() & "/Encode/mb" & streamHandle & ".wav"

                'Dim fileDuration As Double = 0
                'fileDuration = Bass.GetDecodedDuration(streamHandle)
                'LogInformation("PlayToDevice", "fileDuration=" & fileDuration)
                'Return True

                If Settings.LogDebugInfo Then
                    LogInformation("PlayToDevice", "Nowplaying fileurl=" & url)
                    LogInformation("PlayToDevice", "HQPlyaer url=" & hqpUrl)
                End If

                Dim hqpMsg As String = hqpInterface.HQP_Stop(True)
                If (Settings.LogDebugInfo) Then
                    LogInformation("PlayToDevice-Stop", "status=" & hqpMsg)
                End If

                hqpMsg = hqpInterface.HQP_PlaylistAdd(hqpUrl, True, True, True)
                If (Settings.LogDebugInfo) Then
                    LogInformation("PlayToDevice-PlaylistAdd", "status=" & hqpMsg & ",url=" & hqpUrl)
                End If
                If IsNothing(hqpMsg) Then
                    Return False
                End If

                currentPlayStartTimeEstimated = True
                lastUserInitiatedStop = DateTime.UtcNow.Ticks

                hqpMsg = hqpInterface.HQP_Play(True)
                If (Settings.LogDebugInfo) Then
                    LogInformation("PlayToDevice-play", "status=" & hqpMsg)
                End If
                If Not IsNothing(hqpMsg) Then
                    SyncLock resourceLock
                        If currentPlayStartTimeEstimated Then
                            currentPlayStartTicks = DateTime.UtcNow.Ticks
                        End If
                    End SyncLock
                    hqpStatusTimer.Change(500, statusTimerInterval)
                    Return True
                End If
            Catch ex As Exception
                LogError(ex, "Play")
            End Try
            Return False
        End Function

        Public Function QueueNext(url As String) As Boolean
            If supportSetNextInQueue Then
            End If
            Return False
        End Function

        Public Sub PausePlayback()
            If currentPlayState = PlayState.Playing Then
                Try
                    'stop playback instead of pause
                    StopPlayback(True)
                    Exit Sub

                    hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
                    currentPlayPositionMs = CInt((DateTime.UtcNow.Ticks - currentPlayStartTicks) \ TimeSpan.TicksPerMillisecond)
                    Dim hqpMsg As String = hqpInterface.HQP_Pause(True)
                    GetPlayStateInformation()
                Catch ex As Exception
                    LogError(ex, "Pause")
                End Try
            End If
        End Sub

        Public Sub ResumePlayback()
            If currentPlayState = PlayState.Paused Then
                Try
                    Dim hqpMsg As String = hqpInterface.HQP_Play(True, True)
                    currentPlayStartTicks = DateTime.UtcNow.Ticks - currentPlayPositionMs * TimeSpan.TicksPerMillisecond
                    If (Settings.LogDebugInfo) Then
                        LogInformation("ResumePlayback", "status=" & hqpMsg)
                    End If
                    GetPlayStateInformation()
                    hqpStatusTimer.Change(0, statusTimerInterval)
                Catch ex As Exception
                    LogError(ex, "Resume")
                End Try
            End If
        End Sub

        Public Sub StopPlayback(Optional raiseNotification As Boolean = False)
            hqpStatusTimer.Change(Timeout.Infinite, Timeout.Infinite)
            playToMode = False
            If currentPlayState <> PlayState.Stopped Then
                Try
                    lastUserInitiatedStop = DateTime.UtcNow.Ticks
                    queueNextTrackPending = False
                    hqpInterface.HQP_Stop(True)
                    currentPlayPositionMs = 0
                    If raiseNotification Then
                        currentPlayState = Plugin.PlayState.Stopped
                        SyncNewPlayState()
                    End If
                Catch ex As Exception
                    If isActive Then
                        LogError(ex, "Stop")
                    End If
                End Try
            End If
        End Sub

        Public Function Seek(positionMs As Integer) As Boolean
            Try
                Dim value As New TimeSpan(positionMs * TimeSpan.TicksPerMillisecond)

                If (value.Ticks >= currentTrackDurationTicks) Then
                    currentPlayStartTicks = Long.MaxValue
                    If mbApiInterface.Player_PlayNextTrack() Then
                        Return False
                    End If
                    Return True
                End If

                If (Not hqpProfile.SeekEnabled) Then
                    Return False
                End If

                'hqpInterface.HQP_Seek(CInt(value.TotalSeconds), True)
                hqpInterface.HQP_Seek(CInt(value.TotalSeconds))
                SyncLock resourceLock
                    'GetPlayStateInformation()
                    'currentPlayStartTicks = DateTime.UtcNow.Ticks - (currentPlayPositionMs * TimeSpan.TicksPerMillisecond)
                    currentPlayStartTicks = DateTime.UtcNow.Ticks - value.Ticks
                End SyncLock
                Return True
            Catch ex As Exception
                LogError(ex, "Seek", ex.StackTrace)
                Return False
            End Try
            Return True
        End Function

        Private Sub OnHqpStatusCheck(parameters As Object)
            Try
                Dim syncPlayState As Boolean = False
                SyncLock resourceLock
                    Dim oldPlayState As PlayState = currentPlayState
                    Dim ex As Exception = GetPlayStateInformation()
                    If ex Is Nothing Then
                        currentErrorCount = 0
                    ElseIf TypeOf ex Is SocketException AndAlso DirectCast(ex, SocketException).SocketErrorCode = SocketError.ConnectionRefused Then
                        currentErrorCount += 1
                        If currentErrorCount = 3 Then
                            LogInformation("StateTimer", "3 connection refusals")
                            statusTimerInterval = 750
                            hqpStatusTimer.Change(0, statusTimerInterval)
                        ElseIf currentErrorCount = 6 Then
                            LogInformation("StateTimer", "6 connection refusals")
                            statusTimerInterval = 1000
                            hqpStatusTimer.Change(0, statusTimerInterval)
                        ElseIf currentErrorCount >= 10 Then
                            currentPlayState = PlayState.Stopped
                            LogError(ex, "StateTimer", "10 connection refusals")
                        End If
                    Else
                        LogError(ex, "StateTimer")
                    End If
                    If currentPlayState <> oldPlayState Then
                        LogInformation("OnHqpStatusCheck", currentPlayState.ToString & ",old=" & oldPlayState.ToString())
                        syncPlayState = ProcessNewPlayState(oldPlayState)
                    End If
                End SyncLock
                If syncPlayState Then
                    SyncNewPlayState()
                End If
                If currentPlayStartTimeEstimated OrElse queueNextTrackPending Then
                    'GetPlayPositionInformation()
                End If
            Catch
            End Try
        End Sub

    End Class  ' MediaRendererDevice
End Class