Imports System.Text
Imports System.IO
Imports System.Threading
Imports System.Xml
Imports System.Collections.ObjectModel

Partial Public Class Plugin

    Private Enum BrowseFlag
        BrowseMetadata
        BrowseDirectChildren
    End Enum  ' BrowseFlag

    Private Class ItemManager
        Public SupportedMimeTypes() As String = Nothing
        Public DisablePcmTimeSeek As Boolean = False
        Private ReadOnly encodeSettings As New MediaSettings

        Private Shared ReadOnly queryFields() As Plugin.MetaDataType = DirectCast(New Integer() {MetaDataType.Url, MetaDataType.Category, MetaDataType.Artist, MetaDataType.ArtistPeople, MetaDataType.AlbumArtist, MetaDataType.Composer, MetaDataType.Conductor, MetaDataType.TrackTitle, MetaDataType.Album, MetaDataType.TrackNo, MetaDataType.DiscNo, MetaDataType.DiscCount, MetaDataType.YearOnly, MetaDataType.Genre, MetaDataType.Publisher, MetaDataType.Rating, -MetaDataType.Duration, -MetaDataType.FileSize, -MetaDataType.Bitrate, -MetaDataType.SampleRate, -MetaDataType.Channels, -MetaDataType.DateAdded, -MetaDataType.PlayCount, -MetaDataType.DateLastPlayed, -MetaDataType.ReplayGainTrack, 0}, Plugin.MetaDataType())


        Public Sub New()

        End Sub

        Private Shared Function LoadFile(url As String) As String()
            Dim tags() As String
            mbApiInterface.Library_GetFileTags(url, queryFields, tags)
            Dim id As String = GetFileId(url)
            'tags(MetaDataIndex.Id) = id
            Return tags
        End Function


        Public Function TryGetFileInfo(url As String, ByRef duration As TimeSpan) As Boolean
            Dim tags() As String = LoadFile(url)

            Dim durationValue As Long
            If Long.TryParse(tags(MetaDataIndex.Duration), durationValue) Then
                duration = New TimeSpan(durationValue)
            End If
            Return True
        End Function


        Private Function IsCodecSupported(codec As FileCodec) As Boolean
            If codec = FileCodec.Unknown Then
                Return False
            ElseIf SupportedMimeTypes Is Nothing Then
                Return True
            Else
                Dim mimeTypes() As String = GetMimes(codec)
                For index As Integer = 0 To SupportedMimeTypes.Length - 1
                    For index2 As Integer = 0 To mimeTypes.Length - 1
                        If SupportedMimeTypes(index).StartsWith(mimeTypes(index2), StringComparison.OrdinalIgnoreCase) Then
                            Return True
                        End If
                    Next index2
                Next index
                Return False
            End If
        End Function

        Function GetFileExt(Filename As String) As String
            Return Path.GetExtension(Filename)
        End Function

        Public Function GetCodec(extension As String) As FileCodec
            If String.IsNullOrEmpty(extension) Then
                Return FileCodec.Unknown
            Else
                Select Case extension
                    Case ".mp3"
                        Return FileCodec.Mp3
                    Case ".m4a", ".m4b", ".mp2", ".mp4"
                        Return FileCodec.Aac
                    Case ".aac"
                        Return FileCodec.AacNoContainer
                    Case ".wma"
                        Return FileCodec.Wma
                    Case ".opus"
                        Return FileCodec.Opus
                    Case ".ogg", ".oga"
                        Return FileCodec.Ogg
                    Case ".spx"
                        Return FileCodec.Spx
                    Case ".flac"
                        Return FileCodec.Flac
                    Case ".wv"
                        Return FileCodec.WavPack
                    Case ".tak"
                        Return FileCodec.Tak
                    Case ".mpc", ".mp+", ".mpp"
                        Return FileCodec.Mpc
                    Case ".wav"
                        Return FileCodec.Wave
                    Case ".aiff"
                        Return FileCodec.Aiff
                    Case ".pcm"
                        Return FileCodec.Pcm
                    Case Else
                        Return FileCodec.Unknown
                End Select
            End If
        End Function

        'Private Function GetExtension(codec As FileCodec) As String
        '    Select Case codec
        '        Case FileCodec.Mp3
        '            Return ".mp3"
        '        Case FileCodec.Aac, FileCodec.Alac
        '            Return ".m4a"
        '        Case FileCodec.AacNoContainer
        '            Return ".aac"
        '        Case FileCodec.Wma
        '            Return ".wma"
        '        Case FileCodec.Ogg
        '            Return ".ogg"
        '        Case FileCodec.Flac
        '            Return ".flac"
        '        Case FileCodec.WavPack
        '            Return ".wv"
        '        Case FileCodec.Wave
        '            Return ".wav"
        '        Case FileCodec.Tak
        '            Return ".tak"
        '        Case FileCodec.Mpc
        '            Return ".mpc"
        '        Case FileCodec.Aiff
        '            Return ".aiff"
        '        Case Else
        '            Return ".pcm"
        '    End Select
        'End Function

        Private Function GetMime(codec As FileCodec, bitDepth As Integer) As String
            Dim mimes() As String = GetMimes(codec)
            If codec = FileCodec.Pcm Then
                mimes = New String() {mimes(If(bitDepth <> 24, 0, 1))}
            End If
            If SupportedMimeTypes Is Nothing Then
                If mimes.Length > 0 Then
                    Return mimes(0)
                End If
                Return ""
            Else
                For index As Integer = 0 To mimes.Length - 1
                    For index2 As Integer = 0 To SupportedMimeTypes.Length - 1
                        If SupportedMimeTypes(index2).StartsWith(mimes(index), StringComparison.OrdinalIgnoreCase) Then
                            Return mimes(index)
                        End If
                    Next index2
                Next index
                Select Case codec
                    Case FileCodec.Wave
                        Return "audio/wav"
                    Case FileCodec.Pcm
                        Return "audio/L" & bitDepth
                    Case Else
                        Return ""
                End Select
            End If
        End Function

        Private Function GetMimes(codec As FileCodec) As String()
            Select Case codec
                Case FileCodec.Mp3
                    Return New String() {"audio/mpeg", "audio/mp3", "audio/x-mp3"}
                Case FileCodec.Aac, FileCodec.Alac
                    Return New String() {"audio/m4a", "audio/mp4"}
                Case FileCodec.AacNoContainer
                    Return New String() {"audio/aac", "audio/x-aac"}
                Case FileCodec.Wma
                    Return New String() {"audio/x-ms-wma", "audio/wma"}
                Case FileCodec.Ogg
                    Return New String() {"audio/x-ogg", "audio/ogg", "application/ogg"}
                Case FileCodec.Flac
                    Return New String() {"audio/x-flac", "audio/flac"}
                Case FileCodec.WavPack
                    Return New String() {"audio/x-wavpack", "audio/wavpack"}
                Case FileCodec.Wave
                    Return New String() {"audio/wav", "audio/x-wav"}
                Case FileCodec.Tak
                    Return New String() {"audio/x-tak", "audio/tak"}
                Case FileCodec.Mpc
                    Return New String() {"audio/x-musepack", "audio/musepack"}
                Case FileCodec.Aiff
                    Return New String() {"audio/x-aiff", "audio/aiff"}
                Case FileCodec.Pcm
                    Return New String() {"audio/L16", "audio/L24"}
                Case Else
                    Return New String() {}
            End Select
        End Function

        Private Function GetDlnaType(codec As FileCodec) As String
            Select Case codec
                Case FileCodec.Mp3
                    Return "DLNA.ORG_PN=MP3;"
                Case FileCodec.Aac, FileCodec.AacNoContainer
                    Return "DLNA.ORG_PN=AAC_ISO;"
                Case FileCodec.Wma
                    Return "DLNA.ORG_PN=WMABASE;"
                Case FileCodec.Pcm
                    Return "DLNA.ORG_PN=LPCM;"
                Case Else
                    Return String.Empty
            End Select
        End Function

        Public Function GetFileFeature(url As String, disableSeek As Boolean) As String
            Return GetFileFeature(GetCodec(url), disableSeek)
        End Function

        Private Function GetFileFeature(codec As FileCodec, disableSeek As Boolean) As String
            Return GetDlnaType(codec) & If(disableSeek, "DLNA.ORG_OP=00;", If(DisablePcmTimeSeek, "DLNA.ORG_OP=01;", "DLNA.ORG_OP=11;")) & "DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000"
        End Function

        Public Function GetEncodeFeature(codec As FileCodec, disableSeek As Boolean) As String
            'DLNA.ORG_CI=1;
            Return GetDlnaType(codec) & If(disableSeek, "DLNA.ORG_OP=00;", If(codec = FileCodec.Pcm OrElse codec = FileCodec.Wave, If(DisablePcmTimeSeek, "DLNA.ORG_OP=01;", "DLNA.ORG_OP=11;"), "DLNA.ORG_OP=10;")) & "DLNA.ORG_CI=1;DLNA.ORG_FLAGS=01700000000000000000000000000000"
        End Function

        Public Function GetContinuousStreamFeature(codec As FileCodec) As String
            Return GetDlnaType(codec) & "DLNA.ORG_OP=00;DLNA.ORG_CI=1;DLNA.ORG_FLAGS=01700000000000000000000000000000"
        End Function

        Private Shared Function GetFileId(url As String) As String
            Return (Hex(StringComparer.OrdinalIgnoreCase.GetHashCode(url)) & Hex(StringComparer.OrdinalIgnoreCase.GetHashCode(IO.Path.GetFileName(url)))).PadLeft(16, "0"c)
        End Function

        Private Enum MetaDataType As Byte
            Id = 1
            Url = 2
            FileKind = 4
            FileSize = 7
            Channels = 8
            SampleRate = 9
            Bitrate = 10
            DateModified = 11
            DateAdded = 12
            DateLastPlayed = 13
            PlayCount = 14
            DiscNo = 52
            DiscCount = 54
            Duration = 16
            Category = 42
            TrackTitle = 65
            Name = 65
            Album = 30
            Artist = 32
            ArtistPeople = 33
            AlbumArtist = 31
            Composer = 43
            Conductor = 45
            Genre = 59
            Publisher = 73
            Rating = 75
            TrackNo = 86
            TrackCount = 87
            Year = 88
            YearOnly = 35
            ReplayGainTrack = 94
            ReplayGainAlbum = 95
        End Enum  ' MetaDataType

        Private Enum MetaDataIndex
            None = -1
            Url = 0
            Category = 1
            Artist = 2
            ArtistPeople = 3
            AlbumArtist = 4
            Composer = 5
            Conductor = 6
            Title = 7
            Album = 8
            TrackNo = 9
            DiscNo = 10
            DiscCount = 11
            Year = 12
            Genre = 13
            Publisher = 14
            Rating = 15
            Duration = 16
            Size = 17
            Bitrate = 18
            SampleRate = 19
            Channels = 20
            DateAdded = 21
            PlayCount = 22
            DateLastPlayed = 23
            ReplayGainTrack = 24
            AlbumArtistAndAlbum = 25
            Id = 26
        End Enum  ' MetaDataIndex

        Private Enum MediaType
            Image
            Audio
            Video
            Playlist
            Other
        End Enum  ' MediaType


        Private Class MediaSettings
            Public ReadOnly Audio As MediaSettings

            Public Sub New()
                Audio = New MediaSettings(New AudioEncoder(FileCodec.Pcm), New AudioEncoder(FileCodec.Wave), New AudioEncoder(FileCodec.Mp3), New AudioEncoder(FileCodec.Aac), New AudioEncoder(FileCodec.Ogg))
            End Sub

            Public Class MediaSettings
                Public ReadOnly Encoders As ReadOnlyCollection(Of Encoder)

                Public Sub New(ParamArray encoder() As Encoder)
                    Me.Encoders = New ReadOnlyCollection(Of Encoder)(encoder)
                End Sub
            End Class  ' MediaSettingsImage
        End Class  ' MediaSettings
    End Class  ' ItemManager
End Class