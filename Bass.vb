Imports System.Text
Imports System.IO
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Security
Imports System.Runtime.InteropServices
Imports System.Net
Imports System.Net.Sockets

Partial Public Class Plugin
    <SuppressUnmanagedCodeSecurity()> _
    Private Class Bass
        Public Shared Function GetDecodedLength(streamHandle As Integer, duration As Double) As Long
            Return BASS_ChannelSeconds2Bytes(streamHandle, duration)
        End Function

        Public Shared Function GetDecodedDuration(streamHandle As Integer) As Double
            Return BASS_ChannelBytes2Seconds(streamHandle, BASS_ChannelGetLength(streamHandle, 0))
        End Function

        Public Shared Function GetStreamPosition(streamHandle As Integer) As Long
            Return BASS_ChannelGetPosition(streamHandle, 0)
        End Function

        Public Shared Sub SetEncodeStreamPosition(streamHandle As Integer, position As Long)
            BASS_ChannelSetPosition(streamHandle, position, 0)
        End Sub

        Public Shared Function TryGetStreamInformation(streamHandle As Integer, ByRef sampleRate As Integer, ByRef channelCount As Integer, ByRef codec As FileCodec) As Boolean
            Dim info As New BASS_CHANNELINFO
            If Not BASS_ChannelGetInfo(streamHandle, info) Then
                Return False
            Else
                sampleRate = info.freq
                channelCount = info.chans
                Select Case info.ctype
                    Case BASSChannelType.BASS_CTYPE_STREAM_MP3
                        codec = FileCodec.Mp3
                    Case BASSChannelType.BASS_CTYPE_STREAM_AAC
                        codec = FileCodec.Aac
                    Case BASSChannelType.BASS_CTYPE_STREAM_WMA
                        codec = FileCodec.Wma
                    Case Else
                        codec = FileCodec.Unknown
                End Select
                Return True
            End If
        End Function

        Public Shared Sub CloseStream(streamHandle As Integer)
            BASS_StreamFree(streamHandle)
        End Sub

        <Flags()> _
        Public Enum BASSFlag
            BASS_DEFAULT = 0
            BASS_MIXER_BUFFER = &H2000
            BASS_MIXER_DOWNMIX = &H400000
            BASS_MIXER_END = &H10000
            BASS_MIXER_FILTER = &H1000
            BASS_MIXER_LIMIT = &H4000
            BASS_MIXER_MATRIX = &H10000
            BASS_MIXER_NONSTOP = &H20000
            BASS_MIXER_NORAMPIN = &H800000
            BASS_MIXER_PAUSE = &H20000
            BASS_MIXER_RESUME = &H1000
            BASS_MIXER_POSEX = &H2000
            BASS_SAMPLE_FLOAT = &H100
            BASS_SAMPLE_FX = &H80
            BASS_SAMPLE_MONO = 2
            BASS_SAMPLE_SOFTWARE = &H10
            BASS_STREAM_AUTOFREE = &H40000
            BASS_STREAM_BLOCK = &H100000
            BASS_STREAM_DECODE = &H200000
            BASS_STREAM_PRESCAN = &H20000
            BASS_STREAM_RESTRATE = &H80000
            BASS_STREAM_STATUS = &H800000
            BASS_UNICODE = -2147483648
            BASS_ASYNCFILE = &H40000000
        End Enum  ' BASSFlag

        <Flags()> _
        Public Enum BASSChannelType
            BASS_CTYPE_UNKNOWN = 0
            BASS_CTYPE_STREAM_AAC = &H10B00
            BASS_CTYPE_STREAM_AC3 = &H11000
            BASS_CTYPE_STREAM_AIFF = &H10006
            BASS_CTYPE_STREAM_ALAC = &H10E00
            BASS_CTYPE_STREAM_APE = &H10700
            BASS_CTYPE_STREAM_FLAC = &H10900
            BASS_CTYPE_STREAM_MP1 = &H10003
            BASS_CTYPE_STREAM_MP2 = &H10004
            BASS_CTYPE_STREAM_MP3 = &H10005
            BASS_CTYPE_STREAM_MP4 = &H10B01
            BASS_CTYPE_STREAM_MPC = &H10A00
            BASS_CTYPE_STREAM_OGG = &H10002
            BASS_CTYPE_STREAM_SPX = &H10C00
            BASS_CTYPE_STREAM_WAV = &H40000
            BASS_CTYPE_STREAM_PCM = &H50001
            BASS_CTYPE_STREAM_WMA = &H10300
            BASS_CTYPE_STREAM_WV = &H10500
        End Enum  ' BASSChannelType

        <StructLayout(LayoutKind.Sequential)> _
        Private Class BASS_CHANNELINFO
            Public freq As Integer
            Public chans As Integer
            Public flags As BASSFlag
            Public [ctype] As BASSChannelType
            Public origres As Integer
            Public plugin As Integer
            Public sample As Integer
            Private filenamePtr As IntPtr
        End Class  ' BASS_CHANNELINFO 

        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelSeconds2Bytes(handle As Integer, pos As Double) As Long
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelBytes2Seconds(handle As Integer, pos As Long) As Double
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelGetInfo(handle As Integer, <Out()> info As BASS_CHANNELINFO) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelGetLength(handle As Integer, mode As Integer) As Long
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelGetPosition(handle As Integer, mode As Integer) As Long
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_ChannelSetPosition(handle As Integer, pos As Long, mode As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
        <DllImport("bassmix.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_Mixer_ChannelSetPosition(handle As Integer, pos As Long, mode As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
        <DllImport("bass.dll", CharSet:=CharSet.Auto)> _
        Private Shared Function BASS_StreamFree(handle As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
    End Class  ' Bass
End Class