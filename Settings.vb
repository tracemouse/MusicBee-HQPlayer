Imports System.Drawing
Imports System.Threading

Partial Public Class Plugin
    Private Class Settings
        Public Shared ServerName As String = "MusicBee HQP Server"
        Public Shared EnablePlayToDevice As Boolean = True
        Public Shared IpAddress As String = ""
        Public Shared ServerPort As Integer = 16888
        Public Shared BitDepth As Integer = 16
        Public Shared SampleRate As Integer = 44100
        Public Shared Channel As Integer = 2
        Public Shared Udn As Guid = Guid.Empty
        Public Shared DefaultProfileIndex As Integer = 0
        Public Shared HQPProfiles As New List(Of HQPProfile)
        Public Shared ServerReplayGainMode As ReplayGainMode = ReplayGainMode.Off
        Public Shared ServerEnableSoundEffects As Boolean = False
        Public Shared ServerUpdatePlayStatistics As Boolean = False
        Public Shared ContinuousOutput As Boolean = False
        Public Shared Logfilename As String = mbApiInterface.Setting_GetPersistentStoragePath() & "MBHQPLog.txt"
        Public Shared LogDebugInfo As Boolean = False
        Public Shared LogDebugSocketMsg As Boolean = False



        Shared Sub New()
            Dim settingsUrl As String = mbApiInterface.Setting_GetPersistentStoragePath() & "HQPSettings.dat"
            'If (LogDebugInfo) Then
            'LogInformation("Settings", "settings file=" & settingsUrl)
            'End If

            Dim profile As HQPProfile
            If IO.File.Exists(settingsUrl) Then
                Try
                    Using stream As New IO.FileStream(settingsUrl, IO.FileMode.Open, IO.FileAccess.Read),
                          reader As New IO.BinaryReader(stream)
                        Dim version As Integer = reader.ReadInt32()
                        ServerName = reader.ReadString()
                        EnablePlayToDevice = reader.ReadBoolean()
                        ServerPort = reader.ReadInt32()
                        DefaultProfileIndex = reader.ReadInt32()
                        ServerReplayGainMode = DirectCast(reader.ReadInt32(), ReplayGainMode)
                        ServerUpdatePlayStatistics = reader.ReadBoolean()
                        ContinuousOutput = reader.ReadBoolean()
                        LogDebugInfo = reader.ReadBoolean()
                        Dim profileCount As Integer = reader.ReadInt32()
                        For index As Integer = 0 To profileCount - 1
                            profile = New HQPProfile
                            profile.udn = Guid.Parse(reader.ReadString())
                            profile.ProfileName = reader.ReadString()
                            profile.IpAddress = reader.ReadString()
                            profile.product = reader.ReadString()
                            profile.version = reader.ReadString()
                            profile.platform = reader.ReadString()
                            HQPProfiles.Add(profile)
                        Next index
                        If version >= 2 Then
                            Udn = New Guid(reader.ReadString())
                            If version >= 3 Then
                                IpAddress = reader.ReadString()
                            End If
                        End If
                    End Using
                Catch
                End Try
            End If
            If HQPProfiles.Count = 0 Then
                profile = New HQPProfile
                profile.ProfileName = "HQPlayer(localhost)"
                profile.IpAddress = "127.0.0.1"
                HQPProfiles.Add(profile)
            End If

            If Udn = Guid.Empty Then
                Udn = Guid.NewGuid()
                SaveSettings()
            End If

        End Sub

        Public Shared Sub SaveSettings()
            Dim settingsUrl As String = mbApiInterface.Setting_GetPersistentStoragePath() & "HQPSettings.dat"
            Try
            Finally
                Using stream As New IO.FileStream(settingsUrl, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None),
                      writer As New IO.BinaryWriter(stream)
                    writer.Write(4)
                    writer.Write(ServerName)
                    writer.Write(EnablePlayToDevice)
                    writer.Write(ServerPort)
                    writer.Write(DefaultProfileIndex)
                    writer.Write(ServerReplayGainMode)
                    writer.Write(ServerUpdatePlayStatistics)
                    writer.Write(ContinuousOutput)
                    writer.Write(LogDebugInfo)
                    writer.Write(HQPProfiles.Count)
                    For index As Integer = 0 To HQPProfiles.Count - 1
                        Dim profile As HQPProfile = HQPProfiles(index)
                        writer.Write(profile.udn.ToString())
                        writer.Write(profile.ProfileName)
                        writer.Write(profile.IpAddress)
                        writer.Write(profile.product)
                        writer.Write(profile.version)
                        writer.Write(profile.platform)
                    Next index
                    writer.Write(Udn.ToString())
                    writer.Write(IpAddress)
                End Using
            End Try
        End Sub
    End Class  ' Settings
End Class
