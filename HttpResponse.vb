Imports System.Text
Imports System.IO
Imports System.Net.Sockets
Imports System.Xml

Partial Public Class Plugin
    Private Class HttpResponse
        Public StateCode As Integer = 200
        Private ReadOnly request As HttpRequest
        Private ReadOnly socketStream As NetworkStream
        Private ReadOnly headers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private responseSent As Boolean = False
        Private chunkedStream As ChunkedStream

        Public Sub New(request As HttpRequest, stream As NetworkStream)
            Me.request = request
            Me.socketStream = stream
        End Sub

        Public Sub AddHeader(key As HttpHeader, value As String)
            Select Case key
                Case HttpHeader.AcceptRanges : headers.Add("Accept-Ranges", value)
                Case HttpHeader.ContentLength : headers.Add("Content-Length", value)
                Case HttpHeader.ContentType : headers.Add("Content-Type", value)
                Case HttpHeader.ContentEncoding : headers.Add("Content-Encoding", value)
                Case HttpHeader.ContentDisposition : headers.Add("Content-Disposition", value)
                Case HttpHeader.Date : headers.Add("Date", value)
                Case HttpHeader.Server : headers.Add("Server", value)
                Case HttpHeader.Connection : headers.Add("Connection", value)
                Case HttpHeader.CacheControl : headers.Add("Cache-Control", value)
                Case HttpHeader.KeepAlive : headers.Add("Keep-Alive", value)
            End Select
        End Sub

        Public Sub AddHeader(key As String, value As String)
            headers.Add(key, value)
        End Sub

        Public Sub SendHeaders()
            If responseSent Then Exit Sub
            AddHeader(HttpHeader.Date, DateTime.Now.ToString("r"))
            'AddHeader(HttpHeader.Connection, "close")
            Dim data() As Byte = Encoding.ASCII.GetBytes(String.Format("{0} {1} {2}" & ControlChars.CrLf, request.Version, StateCode, GetState()))
            socketStream.Write(data, 0, data.Length)
            For Each item As KeyValuePair(Of String, String) In headers
                data = Encoding.ASCII.GetBytes(String.Format("{0}: {1}" & ControlChars.CrLf, item.Key, item.Value))
                socketStream.Write(data, 0, data.Length)
            Next item
            data = Encoding.ASCII.GetBytes(ControlChars.CrLf)
            socketStream.Write(data, 0, data.Length)
            responseSent = True
            Dim transferEncoding As String
            If headers.TryGetValue("Transfer-Encoding", transferEncoding) AndAlso String.Compare(transferEncoding.Trim(""""c), "chunked", StringComparison.OrdinalIgnoreCase) = 0 Then
                If Settings.LogDebugInfo Then
                    LogInformation("httpresponse", "chunked stream")
                End If
                chunkedStream = New ChunkedStream(socketStream, False)
            End If
        End Sub

        'Public Sub SendStatusHeader()
        '    Dim data() As Byte = Encoding.ASCII.GetBytes(String.Format("{0} {1} {2}" & ControlChars.CrLf, request.Version, StateCode, GetState()))
        '    sourceStream.Write(data, 0, data.Length)
        '    data = Encoding.ASCII.GetBytes(ControlChars.CrLf)
        '    sourceStream.Write(data, 0, data.Length)
        '    responseSent = True
        'End Sub

        Public Function GetState() As String
            Select Case StateCode
                Case 200 : Return "OK"
                Case 206 : Return "Partial Content"
                Case 400 : Return "Bad Request"
                Case 402 : Return "Payment Required"
                Case 404 : Return "Not Found"
                Case 406 : Return "Not Acceptable"
                Case 500 : Return "Internal Server Error"
            End Select
            Return String.Empty
        End Function

        Public ReadOnly Property Stream() As Stream
            Get
                Return If(chunkedStream Is Nothing, DirectCast(socketStream, Stream), DirectCast(chunkedStream, Stream))
            End Get
        End Property

        Public Sub CloseStream()
            If Stream IsNot Nothing Then
                Stream.Close()
            End If
        End Sub
    End Class  ' HttpResponse

    Private Enum HttpHeader
        ContentLength
        ContentType
        Server
        [Date]
        Connection
        AcceptRanges
        ContentEncoding
        CacheControl
        ContentDisposition
        KeepAlive
    End Enum  ' HttpHeader

    Private NotInheritable Class HttpException
        Inherits Exception
        Public ReadOnly Code As Integer
        Public Sub New(code As Integer, message As String)
            MyBase.New(message)
            Me.Code = code
        End Sub
    End Class  ' HttpException

End Class
