Imports System.Text
Imports System.IO
Imports System.Threading
Imports System.Xml
Imports System.Runtime.InteropServices
Imports System.Net
Imports System.Net.Sockets

Partial Public Class Plugin
    Private Shared ReadOnly renderingDevices As New List(Of MediaRendererDevice)
    Private Shared activeRenderingDevice As MediaRendererDevice

    Private Class ControlPointManager
        Private started As Boolean = False

        Public Sub Dispose()
            [Stop]()
        End Sub

        Public Sub Start()
            Try
                UpdateRenderingDevices()
            Catch ex As Exception
                LogError(ex, "ControlPointManager:Start")
            End Try
        End Sub

        Public Sub [Stop]()
            AudioEncoder.StopEncode()

            started = False
            SyncLock renderingDevices
                For Each device As MediaRendererDevice In renderingDevices
                    device.Dispose()
                Next device
                renderingDevices.Clear()
                activeRenderingDevice = Nothing
            End SyncLock
        End Sub

        Public Sub Restart()
            If activeRenderingDevice IsNot Nothing Then
                activeRenderingDevice.StopPlayback(True)
            End If
            If Not Settings.EnablePlayToDevice Then
                [Stop]()
            ElseIf Not started Then
                UpdateRenderingDevices()
            End If
        End Sub

        Private Sub UpdateRenderingDevices()
            Try
                Dim devicesChanged As Boolean = False
                SyncLock renderingDevices
                    For index As Integer = 0 To Settings.HQPProfiles.Count - 1
                        Dim profile As HQPProfile = Settings.HQPProfiles(index)
                        Dim udn As String = profile.udn.ToString()
                        Dim deviceIndex As Integer = -1
                        For index1 As Integer = 0 To renderingDevices.Count - 1
                            'If renderingDevices(index1).Udn.Contains(udn) Then
                            If String.Compare(renderingDevices(index1).FriendlyName, profile.ProfileName, StringComparison.Ordinal) = 0 Then
                                deviceIndex = index1
                                Exit For
                            End If
                        Next index1
                        If deviceIndex = -1 Then
                            devicesChanged = True
                            renderingDevices.Add(New MediaRendererDevice(profile))
                            If Settings.LogDebugInfo Then
                                LogInformation("UpdateRenderingDevices ", "device:" & profile.ProfileName & "' was added")
                            End If
                        Else
                            If String.Compare(renderingDevices(deviceIndex).hqpProfile.IpAddress, profile.ProfileName, StringComparison.Ordinal) = 0 Then
                                devicesChanged = True
                                renderingDevices(deviceIndex).hqpProfile = profile
                                If activeRenderingDevice.FriendlyName = renderingDevices(deviceIndex).FriendlyName Then
                                    activeRenderingDevice.hqpProfile = profile
                                End If
                            End If
                        End If
                    Next index

                    For index1 As Integer = 0 To renderingDevices.Count - 1
                        Dim device As MediaRendererDevice = renderingDevices(index1)
                        Dim deviceIndex As Integer = -1
                        For index As Integer = 0 To Settings.HQPProfiles.Count - 1
                            Dim profile As HQPProfile = Settings.HQPProfiles(index)
                            If String.Compare(device.FriendlyName, profile.ProfileName, StringComparison.Ordinal) = 0 Then
                                deviceIndex = index
                                Exit For
                            End If
                        Next index
                        If deviceIndex = -1 Then
                            devicesChanged = True
                            If activeRenderingDevice IsNot Nothing AndAlso String.Compare(activeRenderingDevice.FriendlyName, device.FriendlyName, StringComparison.Ordinal) = 0 Then
                                activeRenderingDevice = Nothing
                            End If
                            device.Activate(False)
                            renderingDevices.RemoveAt(index1)
                            If Settings.LogDebugInfo Then
                                LogInformation("UpdateRenderingDevices ", "device:" & device.FriendlyName & "' was removed")
                            End If
                        End If
                    Next index1
                End SyncLock

                If devicesChanged Then
                    mbApiInterface.MB_SendNotification(CallbackType.RenderingDevicesChanged)
                End If
            Catch ex As Exception
                LogError(ex, "UpdateRenderingDevices", ex.StackTrace)
            End Try

        End Sub

    End Class  ' ControlPointManager


End Class