Imports System.Xml
Imports System.Text
Imports System.Runtime.Remoting.Messaging
Imports System.Reflection
Imports System.Globalization

Partial Public Class Plugin
    Private MustInherit Class HQPDevice
        Public ReadOnly Udn As Guid
        Public State As DeviceState = DeviceState.NotStarted
        Protected ReadOnly server As HQPServer

        Public Sub New(udn As Guid)
            Me.Udn = udn
            server = New HQPServer(Me)
        End Sub

        Public Overridable Sub Start()
            If State <> DeviceState.NotStarted Then
                Exit Sub
            End If
            State = DeviceState.Starting
            Try
                server.Start()
            Catch ex As Exception
                State = DeviceState.NotStarted
                LogError(ex, "HQPDevice:Start")
                Throw
            End Try
            State = DeviceState.Started
        End Sub

        Public Overridable Sub [Stop]()
            If State <> DeviceState.Started Then
                Exit Sub
            End If
            State = DeviceState.Stopping
            server.Stop()
            State = DeviceState.NotStarted
        End Sub

        Public Sub Restart(includeHttpServer As Boolean)
            State = DeviceState.Starting
            Try
                server.Restart(includeHttpServer)
            Catch
                State = DeviceState.NotStarted
                Throw
            End Try
            State = DeviceState.Started
        End Sub

    End Class

    Private Enum DeviceState
        NotStarted = 0
        Starting
        Started
        Stopping
    End Enum  ' DeviceState
End Class
