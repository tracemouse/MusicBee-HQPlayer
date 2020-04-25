Imports System.Text
Imports System.Xml
Imports System.IO
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets

Partial Public Class Plugin
    Private Class HQPServer
        Public ReadOnly RootDevice As HQPDevice
        Public ReadOnly HttpServer As HttpServer

        Public Sub New(rootDevice As HQPDevice)
            Me.RootDevice = rootDevice
            HttpServer = New HttpServer(Me)
        End Sub

        Public Sub Start()
            HttpServer.Start()
        End Sub

        Public Sub [Stop]()
            HttpServer.Stop()
        End Sub

        Public Sub Restart(includeHttpServer As Boolean)
            If includeHttpServer Then
                HttpServer.Stop()
                HttpServer.Start()
            End If
        End Sub
    End Class  ' UpnpServer
End Class
