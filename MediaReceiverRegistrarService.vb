Imports System.Text

Partial Public Class Plugin
    <UpnpServiceVariable("AuthorizationDeniedUpdateID", "ui4", True)> _
    <UpnpServiceVariable("A_ARG_TYPE_DeviceID", "string", False)> _
    <UpnpServiceVariable("A_ARG_TYPE_RegistrationRespMsg", "bin.base64", False)> _
    <UpnpServiceVariable("ValidationRevokedUpdateID", "ui4", True)> _
    <UpnpServiceVariable("ValidationSucceededUpdateID", "ui4", True)> _
    <UpnpServiceVariable("A_ARG_TYPE_Result", "int", False)> _
    <UpnpServiceVariable("AuthorizationGrantedUpdateID", "ui4", True)> _
    <UpnpServiceVariable("A_ARG_TYPE_RegistrationReqMsg", "bin.base64", False)> _
    Private NotInheritable Class MediaReceiverRegistrarService
        Inherits UpnpService

        Public Sub New(server As UpnpServer)
            MyBase.New(server, "urn:microsoft.com:service:X_MS_MediaReceiverRegistrar:1", "urn:microsoft.com:serviceId:X_MS_MediaReceiverRegistrar", "/X_MS_MediaReceiverRegistrar.control", "/X_MS_MediaReceiverRegistrar.event", "/X_MS_MediaReceiverRegistrar.xml")
        End Sub

        Protected Overrides Sub WriteEventProperty(writer As System.Xml.XmlWriter)
            writer.WriteStartElement("e", "property", Nothing)
            writer.WriteElementString("AuthorizationDeniedUpdateID", String.Empty)
            writer.WriteEndElement()
            writer.WriteStartElement("e", "property", Nothing)
            writer.WriteElementString("ValidationRevokedUpdateID", String.Empty)
            writer.WriteEndElement()
            writer.WriteStartElement("e", "property", Nothing)
            writer.WriteElementString("ValidationSucceededUpdateID", String.Empty)
            writer.WriteEndElement()
            writer.WriteStartElement("e", "property", Nothing)
            writer.WriteElementString("AuthorizationGrantedUpdateID", String.Empty)
            writer.WriteEndElement()
        End Sub

        <UpnpServiceArgument(0, "RegistrationRespMsg", "A_ARG_TYPE_RegistrationRespMsg")> _
        Private Sub RegisterDevice(request As HttpRequest, <UpnpServiceArgument("A_ARG_TYPE_RegistrationReqMsg")> RegistrationReqMsg As String)
            LogInformation("RegisterDevice", "Not Supported")
            'Throw New SoapException(401, "Invalid Action")
        End Sub

        <UpnpServiceArgument(0, "Result", "A_ARG_TYPE_Result")> _
        Private Sub IsAuthorized(request As HttpRequest, <UpnpServiceArgument("A_ARG_TYPE_DeviceID")> DeviceID As String)
            request.Response.SendSoapHeadersBody(request, "1")
        End Sub

        <UpnpServiceArgument(0, "Result", "A_ARG_TYPE_Result")> _
        Private Sub IsValidated(request As HttpRequest, <UpnpServiceArgument("A_ARG_TYPE_DeviceID")> DeviceID As String)
            request.Response.SendSoapHeadersBody(request, "1")
        End Sub
    End Class  ' MediaReceiverRegistrarService
End Class