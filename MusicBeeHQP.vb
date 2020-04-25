Imports System.Runtime.InteropServices
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Threading

Public Class Plugin
    Private Shared mbApiInterface As New MusicBeeApiInterface
    Private Shared ReadOnly about As New PluginInfo
    Private Const musicBeePluginVersion As String = "1.0"
    Private Shared startTimeTicks As Long
    Private Shared server As MediaServerDevice
    Private Shared controller As ControlPointManager
    Private Shared ReadOnly networkLock As New Object
    Private Shared ReadOnly logLock As New Object
    Private Shared ReadOnly errorCount As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private Shared ReadOnly sendDataBarrier As New SemaphoreSlim(4)
    Private Shared ignoreNamePrefixes() As String = New String() {}
    Private Shared ignoreNameChars As String = Nothing
    Private Shared playCountTriggerPercent As Double
    Private Shared playCountTriggerSeconds As Integer
    Private Shared skipCountTriggerPercent As Double
    Private Shared skipCountTriggerSeconds As Integer
    Private Shared logCounter As Integer = 0
    Private Shared hostAddresses() As IPAddress
    Private Shared subnetMasks()() As Byte
    Private Shared ipOverrideAddressMatched As Boolean
    Private Shared defaultHost As String
    Private Shared localIpAddresses()() As Byte

    Public Function Initialise(ByVal apiInterfacePtr As IntPtr) As PluginInfo
        CopyMemory(mbApiInterface, apiInterfacePtr, Marshal.SizeOf(mbApiInterface))
        about.PluginInfoVersion = PluginInfoVersion
        about.Name = "Stream to HQPlayer"
        about.Description = "A plugin to stream music to HQPlayer"
        about.Author = "Tracemouse"
        about.TargetApplication = ""
        about.Type = PluginType.DataStream
        about.VersionMajor = 1
        about.VersionMinor = 0
        about.Revision = 0
        about.MinInterfaceVersion = MinInterfaceVersion
        about.MinApiRevision = MinApiRevision
        about.ReceiveNotifications = (ReceiveNotificationFlags.TagEvents Or ReceiveNotificationFlags.PlayerEvents)
        about.ConfigurationPanelHeight = 0
        Return about
    End Function

    Public Function Configure(ByVal panelHandle As IntPtr) As Boolean
        Using dialog As New SettingsDialog
            dialog.ShowDialog(Form.FromHandle(mbApiInterface.MB_GetWindowHandle()))
        End Using
        Return True
    End Function

    Public Sub SaveSettings()
    End Sub

    Public Sub Close(ByVal reason As PluginCloseReason)
        RemoveHandler NetworkChange.NetworkAddressChanged, AddressOf NetworkChange_NetworkAddressChanged
        Dim closeThread As New Thread(AddressOf ExecuteClose)
        closeThread.IsBackground = True
        closeThread.Start()
    End Sub

    Private Sub ExecuteClose()
        If controller IsNot Nothing Then
            Try
                controller.Dispose()
            Catch
            End Try
        End If
    End Sub

    Public Sub Uninstall()
    End Sub

    Public Sub ReceiveNotification(ByVal sourceFileUrl As String, ByVal type As NotificationType)
        Select Case type
            Case NotificationType.PluginStartup
                Dim value As Object
                mbApiInterface.Setting_GetValue(SettingId.IgnoreNamePrefixes, value)
                ignoreNamePrefixes = DirectCast(value, String())
                mbApiInterface.Setting_GetValue(SettingId.IgnoreNameChars, value)
                ignoreNameChars = DirectCast(value, String)
                mbApiInterface.Setting_GetValue(SettingId.PlayCountTriggerPercent, value)
                playCountTriggerPercent = DirectCast(value, Integer) / 100
                mbApiInterface.Setting_GetValue(SettingId.PlayCountTriggerSeconds, value)
                playCountTriggerSeconds = DirectCast(value, Integer)
                mbApiInterface.Setting_GetValue(SettingId.SkipCountTriggerPercent, value)
                skipCountTriggerPercent = DirectCast(value, Integer) / 100
                mbApiInterface.Setting_GetValue(SettingId.SkipCountTriggerSeconds, value)
                skipCountTriggerSeconds = DirectCast(value, Integer)
                Try
                    startTimeTicks = DateTime.UtcNow.Ticks
                    If Settings.LogDebugInfo Then
                        LogInformation("Initialise", DateTime.Now.ToString())
                    End If
                    GetNetworkAddresses()
                    server = New MediaServerDevice(Settings.Udn)
                    server.Start()
                    controller = New ControlPointManager
                    controller.Start()
                    AddHandler NetworkChange.NetworkAddressChanged, AddressOf NetworkChange_NetworkAddressChanged
                Catch ex As Exception
                    LogError(ex, "Initialise", ex.StackTrace)
                End Try
            'Case NotificationType.FileAddedToLibrary, NotificationType.FileAddedToInbox, NotificationType.FileDeleted, NotificationType.TagsChanged
            Case NotificationType.PlayStateChanged
                If activeRenderingDevice IsNot Nothing Then
                    Select Case mbApiInterface.Player_GetPlayState()
                        Case PlayState.Stopped
                            activeRenderingDevice.StopPlayback()
                        Case PlayState.Paused
                            activeRenderingDevice.PausePlayback()
                        Case PlayState.Playing
                            activeRenderingDevice.ResumePlayback()
                    End Select
                End If
            Case NotificationType.VolumeMuteChanged
                If activeRenderingDevice IsNot Nothing Then
                    activeRenderingDevice.SetMute(mbApiInterface.Player_GetMute())
                End If
            Case NotificationType.VolumeLevelChanged
                If activeRenderingDevice IsNot Nothing Then
                    activeRenderingDevice.SetVolume(mbApiInterface.Player_GetVolume())
                End If
        End Select
    End Sub

    Private Shared Sub NetworkChange_NetworkAddressChanged(sender As Object, e As EventArgs)
        Try
            If Settings.LogDebugInfo Then
                LogInformation("NetworkChange_NetworkAddressChanged", "")
            End If
            SyncLock networkLock
                GetNetworkAddresses()
                If controller IsNot Nothing Then
                    controller.Restart()
                End If
            End SyncLock
        Catch ex As Exception
            LogError(ex, "NetworkChange_NetworkAddressChanged")
        End Try
    End Sub

    Private Shared Sub GetNetworkAddresses()
        ' Dns.GetHostAddresses(Dns.GetHostName()).Where(Function(a) a.AddressFamily = AddressFamily.InterNetwork)
        Dim addressList As New List(Of IPAddress)
        Dim subnetMaskList As New List(Of Byte())
        ipOverrideAddressMatched = String.IsNullOrEmpty(Settings.IpAddress)
        defaultHost = Nothing
        For Each network As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
            If network.OperationalStatus = OperationalStatus.Up Then 'AndAlso network.NetworkInterfaceType <> NetworkInterfaceType.Loopback Then
                'LogInformation("GetNetworkAdresseses", "id=" & network.Name & ",speed=" & network.Speed)
                For Each unicastAddress As UnicastIPAddressInformation In network.GetIPProperties.UnicastAddresses
                    If unicastAddress.Address.AddressFamily = AddressFamily.InterNetwork AndAlso unicastAddress.IPv4Mask IsNot Nothing Then  'IPv4
                        If Not addressList.Contains(unicastAddress.Address) Then
                            If unicastAddress.IsDnsEligible AndAlso defaultHost Is Nothing Then
                                defaultHost = unicastAddress.Address.ToString()
                            End If
                            If Settings.LogDebugInfo Then
                                LogInformation("GetNetworkAddresses", unicastAddress.Address.ToString() & ",dns=" & unicastAddress.IsDnsEligible & ",name=" & network.Name & ",speed=" & network.Speed)
                            End If
                            addressList.Add(unicastAddress.Address)
                            If unicastAddress.Address.ToString() = Settings.IpAddress Then
                                ipOverrideAddressMatched = True
                            End If
                            subnetMaskList.Add(unicastAddress.IPv4Mask.GetAddressBytes())
                        End If
                        Exit For
                    End If
                Next unicastAddress
            End If
        Next network
        hostAddresses = addressList.ToArray()
        subnetMasks = subnetMaskList.ToArray()
        If defaultHost Is Nothing Then
            defaultHost = hostAddresses(0).ToString()
        End If
        'Try
        '    Dim settingsUrl As String = mbApiInterface.Setting_GetPersistentStoragePath() & "UPnPaddress.dat"
        '    If IO.File.Exists(settingsUrl) Then
        '        Using reader As New IO.StreamReader(settingsUrl)
        '            defaultHost = reader.ReadLine()
        '        End Using
        '    End If
        'Catch
        'End Try
        If Settings.LogDebugInfo Then
            LogInformation("GetNetworkAddresses", PrimaryHostUrl)
        End If
        localIpAddresses = New Byte(hostAddresses.Length - 1)() {}
        For index As Integer = 0 To hostAddresses.Length - 1
            localIpAddresses(index) = hostAddresses(index).GetAddressBytes()
        Next index
    End Sub

    Private Shared ReadOnly Property PrimaryHostUrl() As String
        Get
            Return "http://" & If(String.IsNullOrEmpty(Settings.IpAddress), defaultHost, Settings.IpAddress) & ":" & Settings.ServerPort
        End Get
    End Property

    Public Function GetRenderingDevices() As String()
        Dim list As New List(Of String)
        SyncLock renderingDevices
            For Each device As MediaRendererDevice In renderingDevices
                list.Add(device.FriendlyName)
                If (Settings.LogDebugInfo) Then
                    LogInformation("Plugin-GetRenderingDevices", "device name=" & device.FriendlyName)
                End If
            Next device
        End SyncLock
        Return list.ToArray()
    End Function

    Public Function GetRenderingSettings() As Integer()
        Return New Integer() {CInt(Settings.ContinuousOutput), Settings.SampleRate, Settings.Channel, Settings.BitDepth}
    End Function

    Public Function SetActiveRenderingDevice(name As String) As Boolean
        If (Settings.LogDebugInfo) Then
            LogInformation("Plugin-SetActiveRenderingDevice", "device name=" & name)
        End If
        SyncLock renderingDevices
            If name Is Nothing Then
                If activeRenderingDevice IsNot Nothing Then
                    activeRenderingDevice.Activate(False)
                    activeRenderingDevice = Nothing
                End If
                Return True
            Else
                If activeRenderingDevice IsNot Nothing Then
                    If String.Compare(activeRenderingDevice.FriendlyName, name, StringComparison.Ordinal) = 0 Then
                        Return True
                    End If
                    activeRenderingDevice.Activate(False)
                    activeRenderingDevice = Nothing
                End If
                For Each device As MediaRendererDevice In renderingDevices
                    If String.Compare(device.FriendlyName, name, StringComparison.Ordinal) = 0 Then
                        activeRenderingDevice = device
                        If activeRenderingDevice.Activate(True) Then
                            Return True
                        Else
                            activeRenderingDevice = Nothing
                            Return False
                        End If
                    End If
                Next device
            End If
        End SyncLock
        Return False
    End Function

    Public Function PlayToDevice(url As String, streamHandle As Integer) As Boolean
        If activeRenderingDevice Is Nothing Then
            If Settings.LogDebugInfo Then
                LogInformation("PlayToDevice", url & " - no active device")
            End If
            Return False
        Else
            Return activeRenderingDevice.PlayToDevice(url, streamHandle)
        End If
    End Function

    Public Function QueueNext(url As String) As Boolean
        If activeRenderingDevice Is Nothing Then
            Return False
        Else
            Return activeRenderingDevice.QueueNext(url)
        End If
    End Function

    Public Function GetPlayPosition() As Integer
        If activeRenderingDevice Is Nothing Then
            Return 0
        Else
            Return activeRenderingDevice.PlayPositionMs
        End If
    End Function

    Public Sub SetPlayPosition(ms As Integer)
        If activeRenderingDevice IsNot Nothing Then
            activeRenderingDevice.Seek(ms)
        End If
    End Sub

    Private Shared Function LogError(ex As Exception, functionName As String, Optional extra As String = Nothing) As Exception
#If DEBUG Then
        Try
            'Dim logFile As String = mbApiInterface.Setting_GetPersistentStoragePath() & "HQPErrorLog.dat"
            Dim logFile As String = Settings.Logfilename
            Dim counter As Integer = Interlocked.Increment(logCounter)
            Dim gap As Long = (DateTime.UtcNow.Ticks - startTimeTicks) \ TimeSpan.TicksPerMillisecond
            SyncLock logLock
                Dim errorMessage As String = gap & "; " & counter & " " & functionName & " - " & ex.Message
                Debug.WriteLine(errorMessage)
                If Not String.IsNullOrEmpty(extra) Then
                    Debug.WriteLine(extra)
                End If
                If Settings.LogDebugInfo Then
                    Dim count As Integer
                    If Not errorCount.TryGetValue(functionName, count) Then
                        errorCount.Add(functionName, 1)
                    ElseIf count = 3 Then
                        Return ex
                    Else
                        errorCount(functionName) = count + 1
                    End If
                    Using writer As New IO.StreamWriter(logFile, True)
                        writer.WriteLine(errorMessage)
                        If Not String.IsNullOrEmpty(extra) Then
                            writer.WriteLine(extra)
                        End If
                    End Using
                End If
            End SyncLock
        Catch
        End Try
        Return ex
#Else
        If Settings.LogDebugInfo Then
            Try
                Dim counter As Integer = Interlocked.Increment(logCounter)
                Dim gap As Long = (DateTime.UtcNow.Ticks - startTimeTicks) \ TimeSpan.TicksPerMillisecond
                SyncLock logLock
                    Dim count As Integer
                    If Not errorCount.TryGetValue(functionName, count) Then
                        errorCount.Add(functionName, 1)
                    ElseIf count = 3 Then
                        Return ex
                    Else
                        errorCount(functionName) = count + 1
                    End If
                    Using writer As New IO.StreamWriter(mbApiInterface.Setting_GetPersistentStoragePath() & "HQPErrorLog.dat", True)
                        writer.WriteLine(gap & "; " & counter & " " & functionName & " - " & ex.Message)
                        If Not String.IsNullOrEmpty(extra) Then
                            writer.WriteLine(extra)
                        End If
                    End Using
                End SyncLock
            Catch
            End Try
        End If
        Return ex
#End If
    End Function

    Private Shared Sub LogInformation(functionName As String, information As String)
#If DEBUG Then
        Try
            'Dim logFile As String = mbApiInterface.Setting_GetPersistentStoragePath() & "HQPErrorLog.dat"
            Dim logFile As String = Settings.Logfilename
            Dim counter As Integer = Interlocked.Increment(logCounter)
            Dim gap As Long = (DateTime.UtcNow.Ticks - startTimeTicks) \ TimeSpan.TicksPerMillisecond
            SyncLock logLock
                Dim message As String = gap & "; " & counter & " " & functionName & " - " & information
                Debug.WriteLine(message)
                If Settings.LogDebugInfo Then
                    Using writer As New IO.StreamWriter(logFile, True)
                        writer.WriteLine(message)
                    End Using
                End If
            End SyncLock
        Catch
        End Try
#Else
        If Settings.LogDebugInfo Then
            Try
                Dim counter As Integer = Interlocked.Increment(logCounter)
                Dim gap As Long = (DateTime.UtcNow.Ticks - startTimeTicks) \ TimeSpan.TicksPerMillisecond
                SyncLock logLock
                    Using writer As New IO.StreamWriter(mbApiInterface.Setting_GetPersistentStoragePath() & "UpnpErrorLog.dat", True)
                        writer.WriteLine(gap & "; " & counter & " " & functionName & " - " & information)
                    End Using
                End SyncLock
            Catch
            End Try
        End If
#End If
    End Sub


End Class
