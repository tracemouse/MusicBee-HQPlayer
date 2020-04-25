Imports System.Text
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Xml
Imports System.Threading.Tasks
Imports System.Runtime.InteropServices

Partial Public Class Plugin
    Private Delegate Sub HttpRouteDelegate(request As HttpRequest)

    Private Class HttpServer
        Private ReadOnly hqpServer As HQPServer
        Private listenerSocket As TcpListener
        Private listenerThread As Thread
        Private ReadOnly listenerRequests As New List(Of HttpRequest)
        Private ReadOnly listenerTasks As New List(Of Task)
        Private ReadOnly routeTable As New Dictionary(Of String, HttpRouteDelegate)(StringComparer.OrdinalIgnoreCase)

        Public Sub New(hqpServer As HQPServer)
            Me.hqpServer = hqpServer
        End Sub

        Public Sub AddRoute(method As String, path As String, routeDelegate As HttpRouteDelegate)
            routeTable.Add(method & "_" & path, routeDelegate)
        End Sub

        Public Function GetRoute(method As String, path As String) As HttpRouteDelegate
            Dim value As HttpRouteDelegate = Nothing
            If Not routeTable.TryGetValue(method & "_" & path, value) Then
                Return Nothing
            End If
            Return value
        End Function

        Public Sub Start()
            Try
                listenerSocket = New TcpListener(IPAddress.Any, Settings.ServerPort)
                listenerSocket.Start()
            Catch ex As Exception
                LogError(ex, "HttpServer.Start")
                listenerSocket = Nothing
                Throw
            End Try
            listenerThread = New Thread(New ThreadStart(AddressOf Listen))
            listenerThread.IsBackground = True
            listenerThread.Priority = ThreadPriority.BelowNormal
            listenerThread.Start()
        End Sub

        Public Sub [Stop]()
            If listenerSocket IsNot Nothing Then
                AudioEncoder.StopEncode()
                closesocket(listenerSocket.Server.Handle)
                listenerSocket.Server.Close()
                listenerThread.Join()
                Dim pendingTasks() As Task
                SyncLock listenerRequests
                    For index As Integer = 0 To listenerRequests.Count - 1
                        Try
                            listenerRequests(index).Close()
                        Catch
                        End Try
                    Next index
                    pendingTasks = listenerTasks.ToArray()
                End SyncLock
                Task.WaitAll(pendingTasks)
                listenerSocket = Nothing
                listenerThread = Nothing
            End If
        End Sub

        Private Sub Listen()
            Do
                Try
                    Dim socket As TcpClient
                    Try
                        socket = listenerSocket.AcceptTcpClient()
                        If Not socket.Client.Blocking Then
                            LogInformation("Listen", "non-blocking")
                            socket.Client.Blocking = True
                        End If
                    Catch
                        Exit Do
                    End Try
                    ' test the end point is on the same sub-net
                    Dim remoteIpAddress As IPEndPoint = DirectCast(socket.Client.RemoteEndPoint, IPEndPoint)
                    Dim remoteIpAddressBytes() As Byte = remoteIpAddress.Address.GetAddressBytes()
                    Dim matched As Boolean = False
                    For index As Integer = 0 To localIpAddresses.Length - 1
                        Dim localIpAddressBytes() As Byte = localIpAddresses(index)
                        Dim subnetMaskBytes() As Byte = subnetMasks(index)
                        If remoteIpAddressBytes.Length = localIpAddressBytes.Length AndAlso remoteIpAddressBytes.Length = subnetMaskBytes.Length Then
                            matched = True
                            For index2 As Integer = 0 To localIpAddressBytes.Length - 1
                                If (localIpAddressBytes(index2) And subnetMaskBytes(index2)) <> (remoteIpAddressBytes(index2) And subnetMaskBytes(index2)) Then
                                    matched = False
                                    Exit For
                                End If
                            Next index2
                            If matched Then Exit For
                        End If
                    Next index
                    If Not matched Then
                        If Settings.LogDebugInfo Then
                            LogInformation("Listen", "denied " & remoteIpAddress.Address.ToString())
                        End If
                        Continue Do
                    End If
                    socket.SendTimeout = 900000
                    socket.ReceiveTimeout = 900000
                    SyncLock listenerRequests
                        Dim request As New HttpRequest(socket)
                        listenerRequests.Add(request)
                        listenerTasks.Add(Task.Factory.StartNew(AddressOf ProcessRequest, request))
                    End SyncLock
                Catch
                    Thread.Sleep(50)
                End Try
            Loop
        End Sub

        Private Sub ProcessRequest(parameters As Object)
            Dim request As HttpRequest = DirectCast(parameters, HttpRequest)
            Try
                request.ParseHeaders()
                Dim handler As HttpRouteDelegate = GetRoute(request.Method, request.Url)
                If handler Is Nothing Then
                    Dim fragments As String() = request.Url.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
                    For index As Integer = fragments.Length To 0 Step -1
                        Dim value As String = "/"
                        For index2 As Integer = 0 To index - 1
                            value &= fragments(index2) & "/"
                        Next index2
                        value &= "*"
                        handler = GetRoute(request.Method, value)
                        If handler IsNot Nothing Then
                            Exit For
                        End If
                    Next index
                    If handler Is Nothing Then
                        Throw New HttpException(404, "Not found - " + request.Url)
                    End If
                End If
                handler(request)
            Catch ex As HttpException
                LogError(ex, "ProcessRequest:HttpException:" & ex.Code.ToString())
                Try
                    Dim response As HttpResponse = request.Response
                    response.StateCode = ex.Code
                    response.SendHeaders()
                Catch
                End Try
            Catch ex As Exception
                LogError(ex, "ProcessRequest:Exception", ex.StackTrace)
                Try
                    Dim response As HttpResponse = request.Response
                    response.StateCode = 500
                    response.SendHeaders()
                Catch
                End Try
            Finally
                Try
                    request.Close()
                Catch
                Finally
                    SyncLock listenerRequests
                        Dim index As Integer = listenerRequests.IndexOf(request)
                        If index <> -1 Then
                            listenerTasks.RemoveAt(index)
                            listenerRequests.RemoveAt(index)
                        End If
                    End SyncLock
                End Try
            End Try
        End Sub

        <DllImport("ws2_32.dll", CharSet:=CharSet.Unicode)> _
        Private Shared Function closesocket(socketHandle As IntPtr) As Integer
        End Function
    End Class  ' HttpServer
End Class
