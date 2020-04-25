Imports System.Text
Imports System.Xml
Imports System.IO
Imports System.Reflection
Imports System.Net

Partial Public Class Plugin
    Private MustInherit Class HQPService
        Protected ReadOnly server As HQPServer
        Protected ReadOnly serviceTypeName As String
        Protected ReadOnly serviceId As String
        Protected ReadOnly controlUrl As String
        Protected ReadOnly eventSubUrl As String
        Protected ReadOnly SCPDURL As String
        Private descriptionData As Byte()
        Private Delegate Sub SendEventDelegate(sid As String, uri As Uri)

        Public Sub New(server As HQPServer, serviceTypeName As String, serviceId As String, controlUrl As String, eventSubUrl As String, SCPDURL As String)
            Me.server = server
            Me.serviceTypeName = serviceTypeName
            Me.serviceId = serviceId
            Me.controlUrl = controlUrl
            Me.eventSubUrl = eventSubUrl
            Me.SCPDURL = SCPDURL
            Using stream As New IO.MemoryStream,
                  writer As New XmlTextWriter(stream, New UTF8Encoding(False))
                'writer.Formatting = Formatting.Indented
                writer.WriteRaw("<?xml version=""1.0"" encoding=""UTF-8""?>")
                writer.WriteStartElement("scpd", "urn:schemas-upnp-org:service-1-0")
                writer.WriteStartElement("specVersion")
                writer.WriteElementString("major", "1")
                writer.WriteElementString("minor", "0")
                writer.WriteEndElement()
                writer.WriteStartElement("actionList")
                For Each method As MethodInfo In Me.[GetType]().GetMethods(BindingFlags.Instance Or BindingFlags.NonPublic)
                    Dim methodAttribs As IEnumerable(Of UpnpServiceArgument) = method.GetCustomAttributes(GetType(UpnpServiceArgument), True).Cast(Of UpnpServiceArgument)()
                    Dim parameters As ParameterInfo() = method.GetParameters()
                    If methodAttribs.Count() > 0 OrElse parameters.Any(Function(a) a.GetCustomAttributes(GetType(UpnpServiceArgument), True).Length > 0) Then
                        writer.WriteStartElement("action")
                        writer.WriteElementString("name", method.Name)
                        writer.WriteStartElement("argumentList")
                        For Each parameter As ParameterInfo In parameters
                            Dim paramAttrib As UpnpServiceArgument = TryCast(parameter.GetCustomAttributes(GetType(UpnpServiceArgument), True).FirstOrDefault(), UpnpServiceArgument)
                            If paramAttrib IsNot Nothing Then
                                writer.WriteStartElement("argument")
                                writer.WriteElementString("name", parameter.Name)
                                writer.WriteElementString("direction", "in")
                                writer.WriteElementString("relatedStateVariable", paramAttrib.RelatedStateVariable)
                                writer.WriteEndElement()
                            End If
                        Next parameter
                        For Each methodAttrib As UpnpServiceArgument In methodAttribs.OrderBy(Function(a) a.Index)
                            writer.WriteStartElement("argument")
                            writer.WriteElementString("name", methodAttrib.Name)
                            writer.WriteElementString("direction", "out")
                            writer.WriteElementString("relatedStateVariable", methodAttrib.RelatedStateVariable)
                            writer.WriteEndElement()
                        Next methodAttrib
                        writer.WriteEndElement()
                        writer.WriteEndElement()
                    End If
                Next method
                writer.WriteEndElement()
                writer.WriteStartElement("serviceStateTable")
                For Each variable As UpnpServiceVariable In [GetType]().GetCustomAttributes(GetType(UpnpServiceVariable), True).Cast(Of UpnpServiceVariable)()
                    writer.WriteStartElement("stateVariable")
                    writer.WriteAttributeString("sendEvents", If(variable.SendEvents, "yes", "no"))
                    writer.WriteElementString("name", variable.Name)
                    writer.WriteElementString("dataType", variable.DataType)
                    If variable.AllowedValue.Length > 0 Then
                        writer.WriteStartElement("allowedValueList")
                        For Each value As String In variable.AllowedValue
                            writer.WriteElementString("allowedValue", value)
                        Next value
                        writer.WriteEndElement()
                    End If
                    writer.WriteEndElement()
                Next variable
                writer.WriteEndElement()
                writer.WriteEndElement()
                writer.Flush()
                descriptionData = stream.ToArray()
            End Using
            server.HttpServer.AddRoute("POST", controlUrl, New HttpRouteDelegate(AddressOf ProceedControl))
            server.HttpServer.AddRoute("GET", SCPDURL, New HttpRouteDelegate(AddressOf GetDescription))
            server.HttpServer.AddRoute("SUBSCRIBE", eventSubUrl, New HttpRouteDelegate(AddressOf ProceedEventSubcribe))
            server.HttpServer.AddRoute("UNSUBSCRIBE", eventSubUrl, New HttpRouteDelegate(AddressOf ProceedEventUnsubcribe))
        End Sub

        Public ReadOnly Property ServiceType() As String
            Get
                Return serviceTypeName
            End Get
        End Property

        Public Overridable Sub WriteDescription(writer As XmlTextWriter)
            writer.WriteElementString("serviceType", serviceTypeName)
            writer.WriteElementString("serviceId", serviceId)
            writer.WriteElementString("controlURL", controlUrl)
            writer.WriteElementString("eventSubURL", eventSubUrl)
            writer.WriteElementString("SCPDURL", SCPDURL)
        End Sub

        Private Sub GetDescription(request As HttpRequest)
            Dim response As HttpResponse = request.Response
            response.AddHeader(HttpHeader.ContentLength, descriptionData.Length.ToString())
            response.AddHeader(HttpHeader.ContentType, "text/xml; charset=""utf-8""")
            Using stream As New IO.MemoryStream(descriptionData)
                response.SendHeaders()
                stream.CopyTo(response.Stream)
            End Using
        End Sub

        Private Sub ProceedControl(request As HttpRequest)
            Dim soapAction As String
            If Not request.Headers.TryGetValue("SOAPACTION", soapAction) OrElse Not soapAction.Trim(""""c).StartsWith(serviceTypeName, StringComparison.OrdinalIgnoreCase) Then
                LogInformation("ProceedControl", soapAction)
                Throw New HttpException(500, "Service type mismatch")
            End If
            Dim xmlDocument As New XmlDocument
            Using stream As MemoryStream = request.GetContent()
                xmlDocument.Load(stream)
                Dim namespaceManager As New XmlNamespaceManager(xmlDocument.NameTable)
                namespaceManager.AddNamespace("soapNam", "http://schemas.xmlsoap.org/soap/envelope/")
                Dim bodyNode As XmlNode = xmlDocument.SelectSingleNode("/soapNam:Envelope/soapNam:Body/*[1]", namespaceManager)
                If bodyNode Is Nothing Then
                    LogInformation("ProceedControl", soapAction)
                    Throw New HttpException(500, "Body node of SOAP message not found")
                End If
                Dim method As MethodInfo = [GetType]().GetMethod(bodyNode.LocalName, BindingFlags.Instance Or BindingFlags.NonPublic)
                If method Is Nothing Then
                    LogInformation("ProceedControl", "No method: " & soapAction)
                    Throw New SoapException(401, "Invalid Action")
                End If
                Dim outputParameters() As String = method.GetCustomAttributes(GetType(UpnpServiceArgument), True).Cast(Of UpnpServiceArgument)().OrderBy(Function(a) a.Index).[Select](Function(a) a.Name).ToArray()
                Dim parameters() As ParameterInfo = method.GetParameters()
                request.SetSoap(bodyNode.LocalName, serviceTypeName, outputParameters)
                Dim parameterValues() As Object = New Object(parameters.Length - 1) {}
                parameterValues(0) = request
                For index As Integer = 1 To parameters.Length - 1
                    Dim paramNode As XmlNode = bodyNode.SelectSingleNode(parameters(index).Name)
                    If paramNode Is Nothing Then
                        LogInformation("ProceedControl", "No parameters: " & soapAction)
                        Throw New SoapException(402, "Invalid Args")
                    End If
                    parameterValues(index) = paramNode.InnerXml
                Next index
                Try
                    method.Invoke(Me, parameterValues)
                Catch ex As ArgumentException
                    LogError(ex, "Proceed Control", soapAction)
                    Throw New SoapException(402, "Invalid Args")
                Catch ex As TargetParameterCountException
                    LogError(ex, "Proceed Control", soapAction)
                    Throw New SoapException(402, "Invalid Args")
                Catch ex As TargetInvocationException
                    Try
                        soapAction &= Environment.NewLine & Encoding.UTF8.GetString(stream.ToArray())
                    Catch
                    End Try
                    LogError(ex, "Proceed Control", soapAction)
                    If ex.InnerException IsNot Nothing Then
                        LogError(ex.InnerException, "Proceed Control", ex.InnerException.StackTrace)
                    End If
                    If TypeOf ex.InnerException Is SoapException Then
                        Throw ex.InnerException
                    End If
                    Throw New SoapException(501, "Action Failed")
                End Try
            End Using
        End Sub

        Private Sub ProceedEventSubcribe(request As HttpRequest)
            Dim sid As String
            If Not request.Headers.TryGetValue("SID", sid) Then
                sid = "uuid:" & Guid.NewGuid().ToString()
                Dim callback As String = request.Headers("CALLBACK")
                Dim startIndex As Integer = callback.IndexOf("<"c) + 1
                Dim endIndex As Integer = callback.IndexOf(">"c, startIndex)
                Dim uri As New Uri(callback.Substring(startIndex, endIndex - startIndex))
                Dim sendEventHandler As SendEventDelegate = AddressOf SendEvent
                sendEventHandler.BeginInvoke(sid, uri, Nothing, Nothing)
            End If
            Dim response As HttpResponse = request.Response
            response.AddHeader(HttpHeader.ContentLength, "0")
            response.AddHeader("SID", sid)
            response.AddHeader("TIMEOUT", "Second-3600")
            response.SendHeaders()
        End Sub

        Private Sub ProceedEventUnsubcribe(request As HttpRequest)
            request.Response.SendHeaders()
        End Sub

        Private Sub SendEvent(sid As String, uri As Uri)
            Dim content As New StringBuilder
            Dim xmlSettings As New XmlWriterSettings
            xmlSettings.OmitXmlDeclaration = True
            Using writer As XmlWriter = XmlWriter.Create(content, xmlSettings)
                writer.WriteRaw("<?xml version=""1.0""?>")
                writer.WriteStartElement("e", "propertyset", "urn:schemas-upnp-org:event-1-0")
                WriteEventProperty(writer)
                writer.WriteEndElement()
            End Using
            Dim data As Byte() = Encoding.UTF8.GetBytes(content.ToString())
            Dim request As WebRequest = WebRequest.Create(uri)
            request.Method = "NOTIFY"
            request.ContentType = "text/xml; charset=""utf-8"""
            request.ContentLength = data.Length
            request.Headers.Add("NT", "upnp:event")
            request.Headers.Add("NTS", "upnp:propchange")
            request.Headers.Add("SID", sid)
            request.Headers.Add("SEQ", "0")
            request.Proxy = Nothing
            Threading.Thread.Sleep(1000)
            Try
                Dim dataStream As Stream = request.GetRequestStream()
                dataStream.WriteTimeout = 20000
                dataStream.Write(data, 0, data.Length)
                dataStream.Close()
                Using stream As Stream = request.GetResponse().GetResponseStream()
                End Using
            Catch
            End Try
        End Sub

        Protected MustOverride Sub WriteEventProperty(writer As XmlWriter)
    End Class  ' UpnpService
End Class
