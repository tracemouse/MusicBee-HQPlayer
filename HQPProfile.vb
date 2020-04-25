Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Xml

Partial Public Class Plugin

    Private Class HQPProfile

        Public ProfileName As String
        Public IpAddress As String = ""
        Public Port As Integer = 4321
        Public connectTimeout As Integer = 3000
        Public sendTimeout As Integer = 3000
        Public receiveTimeout As Integer = 3000
        Public SupportedExt() As String = {".FLAC", ".WAV", ".DSF", ".DFF"}
        Public SeekEnabled As Boolean = True

        Public IsAlive As Boolean = False
        Public result As Boolean = False

        Public product As String = ""
        Public version As String = ""
        Public platform As String = ""

        Public cmd As HQP_CMD

        Public VolumeMin As Double = -60
        Public VolumeMax As Double = 0
        Public VolumeEnabled As Boolean = True
        Public volume As Double = -10
        Public state As Integer = 0
        Public mode As Integer = 0
        Public filter As Integer
        Public shaper As Integer
        Public rate As Integer
        Public invert As Integer
        Public repeat As Integer
        Public convolution As Integer
        Public random As Integer
        Public track As Integer
        Public min As Integer
        Public sec As Integer
        Public clips As Integer
        Public tracks_total As Integer
        Public queued As Integer
        Public begin_min As Integer
        Public begin_sec As Integer
        Public total_min As Integer
        Public total_sec As Integer
        Public active_mode As Integer
        Public active_rate As Integer
        Public active_mode_str As String
        Public active_filter_str As String
        Public active_shaper_str As String
        Public active_bits As Integer
        Public active_channels As Integer
        Public udn As Guid

        Public Sub New()
            udn = Guid.NewGuid()
        End Sub
        Public Sub New(name As String)
            ProfileName = name
            udn = Guid.NewGuid()
        End Sub
        Public Overrides Function ToString() As String
            Return ProfileName
        End Function

    End Class  ' HQPProfile
End Class
