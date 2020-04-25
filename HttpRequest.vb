Imports System.Text
Imports System.Net.Sockets
Imports System.Threading
Imports System.Web
Imports System.IO

Partial Public Class Plugin
    Private Class HttpRequest
        Public Version As String = ""
        Public Url As String = ""
        Public ReadOnly UrlParams As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Method As String = ""
        Public ReadOnly Headers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public SoapService As String
        Public SoapAction As String
        Public SoapOutParam() As String
        Public Socket As TcpClient
        Private chunkedStream As ChunkedStream
        Private ReadOnly socketStream As NetworkStream
        'Private httpResponse As HttpResponse

        Public Sub New(socket As TcpClient)
            Me.Socket = socket
            Me.socketStream = socket.GetStream()
        End Sub

        Public Sub SetSoap(soapAction As String, soapService As String, soapOutParam As String())
            Me.SoapAction = soapAction
            Me.SoapService = soapService
            Me.SoapOutParam = soapOutParam
        End Sub

        Public Sub ParseHeaders()
            Dim text As New StringBuilder(128)
            Dim isFirstLine As Boolean = True
            Do
                Dim b As Integer = socketStream.ReadByte()
                '' changed to included end of stream error
                If b = -1 Then
                    Throw New HttpException(400, "Bad header")
                ElseIf b = 13 Then
                    ' ignore
                ElseIf b = 10 Then
                    Dim line As String = text.ToString()
                    If line.Length = 0 Then Exit Do
                    text.Length = 0
                    If isFirstLine Then
                        Dim values As String() = line.Split(New Char() {" "c}, 2, StringSplitOptions.RemoveEmptyEntries)
                        Method = values(0).ToUpper()
                        Dim index As Integer = values(1).LastIndexOf(" "c)
                        Version = values(1).Substring(index + 1)
                        values = Uri.UnescapeDataString(values(1).Substring(0, index)).Split(New Char() {"?"c}, 2, StringSplitOptions.RemoveEmptyEntries)
                        Url = values(0).ToLower()
                        If values.Length = 2 Then
                            For Each parameter As String In values(1).Split(New Char() {"&"c}, StringSplitOptions.RemoveEmptyEntries)
                                Dim keyValue As String() = parameter.Split(New Char() {"="c}, 2, StringSplitOptions.RemoveEmptyEntries)
                                UrlParams(keyValue(0)) = If((keyValue.Length <> 2), "", keyValue(1))
                            Next parameter
                        End If
                        isFirstLine = False
                    Else
                        Dim keyValue As String() = line.Split(New Char() {":"c}, 2, StringSplitOptions.RemoveEmptyEntries)
                        Headers(keyValue(0).Trim()) = If(keyValue.Length <= 1, "", keyValue(1).Trim())
                    End If
                Else
                    text.Append(ChrW(b))
                End If
            Loop
            Dim transferEncoding As String
            If Headers.TryGetValue("Transfer-Encoding", transferEncoding) AndAlso String.Compare(transferEncoding.Trim(""""c), "chunked", StringComparison.OrdinalIgnoreCase) = 0 Then
                If Settings.LogDebugInfo Then
                    LogInformation("httprequest", "chunked stream")
                End If
                chunkedStream = New ChunkedStream(socketStream, True)
            End If
        End Sub

        Public Function GetLength() As Integer
            Return Integer.Parse(Headers("Content-Length"))
        End Function

        Public ReadOnly Property Stream() As Stream
            Get
                Return If(chunkedStream Is Nothing, DirectCast(socketStream, Stream), DirectCast(chunkedStream, Stream))
            End Get
        End Property

        Public Sub Close()
            Stream.Close()
            'If httpResponse Is Nothing Then
            '    Stream.Close()
            'Else
            '    httpResponse.CloseStream()
            'End If
            Socket.Close()
        End Sub

        Public Function GetContent() As IO.MemoryStream
            If chunkedStream Is Nothing Then
                Dim buffer As Byte() = New Byte(GetLength() - 1) {}
                Dim readCount As Integer
                Dim offset As Integer = 0
                Do While offset < buffer.Length
                    readCount = socketStream.Read(buffer, offset, buffer.Length - offset)
                    If readCount <= 0 Then
                        Exit Do
                    End If
                    offset += readCount
                Loop
                Return New IO.MemoryStream(buffer, 0, buffer.Length)
            Else
                Dim sourceStream As New IO.MemoryStream
                chunkedStream.CopyTo(sourceStream)
                sourceStream.Flush()
                sourceStream.Position = 0
                Return sourceStream
            End If
        End Function

        Public ReadOnly Property Response() As HttpResponse
            Get
                Return New HttpResponse(Me, socketStream)
                'If httpResponse Is Nothing Then
                '    Return New HttpResponse(Me, socketStream)
                'End If
                'Return httpResponse
            End Get
        End Property
    End Class  ' HttpRequest

    Private NotInheritable Class ChunkedStream
        Inherits Stream
        Private ReadOnly socketStream As NetworkStream
        Private chunkSize As Integer
        Private ReadOnly [readonly] As Boolean

        Public Sub New(socketStream As NetworkStream, canRead As Boolean)
            Me.socketStream = socketStream
            Me.[readonly] = canRead
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return [readonly]
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return Not [readonly]
            End Get
        End Property

        Public Overrides ReadOnly Property CanTimeout As Boolean
            Get
                Return socketStream.CanTimeout
            End Get
        End Property

        Public Overrides Sub Flush()
            socketStream.Flush()
        End Sub

        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException
            End Get
            Set(value As Long)
                Throw New NotSupportedException
            End Set
        End Property

        Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
            If Not CanRead Then
                Throw New NotSupportedException
            End If
            If chunkSize > 0 Then
                Dim minCount As Integer = Math.Min(count, chunkSize)
                Dim readCount As Integer = socketStream.Read(buffer, offset, minCount)
                chunkSize -= readCount
                If chunkSize = 0 Then
                    socketStream.ReadByte()
                    socketStream.ReadByte()
                End If
                Return readCount
            Else
                Dim text As New StringBuilder(128)
                Do
                    Dim b As Integer = socketStream.ReadByte()
                    '' changed to included end of stream error
                    If b = -1 Then
                        Throw New HttpException(400, "Bad chunk header")
                    ElseIf b = 13 Then
                        ' ignore
                    ElseIf b = 10 Then
                        Dim line As String = text.ToString()
                        Dim index As Integer = line.IndexOf(";"c)
                        If index > 0 Then
                            line = line.Substring(0, index).Trim()
                        End If
                        chunkSize = Integer.Parse(line, Globalization.NumberStyles.HexNumber)
                        If chunkSize <= 0 Then
                            chunkSize = 0
                            Return 0
                        Else
                            Return Read(buffer, offset, count)
                        End If
                    Else
                        text.Append(ChrW(b))
                    End If
                Loop
                'Throw New HttpException(400, "Bad chunk header")
            End If
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException
        End Sub

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            If CanRead Then
                Throw New NotSupportedException
            End If
            Dim header() As Byte = Encoding.ASCII.GetBytes(String.Format("{0:X}" & ControlChars.CrLf, count))
            socketStream.Write(header, 0, header.Length)
            socketStream.Write(buffer, offset, count)
            socketStream.Write(New Byte() {13, 10}, 0, 2)
            socketStream.Flush()
        End Sub

        Public Overrides Sub Close()
            If Not CanRead Then
                socketStream.Write(New Byte() {0, 13, 10}, 0, 3)
            End If
            socketStream.Close()
        End Sub
    End Class  ' ChunkedStream
End Class
