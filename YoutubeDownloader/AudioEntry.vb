﻿Imports NAudio.Wave
Imports YoutubeExplode.Models
Imports Xabe.FFmpeg
Imports YoutubeDownloader.GeniusAPI
Public Class AudioEntry
    Dim Downloading As Boolean = False
    Public Video As YoutubeExplode.Models.Video
    Public SpotifyTrack As SpotifyAPI.Web.Models.FullTrack
    Public SpotifyAlbum As SpotifyAPI.Web.Models.FullAlbum
    Public SpotifyTrackAnalysis As SpotifyAPI.Web.Models.AudioAnalysis
    Public MexData As MexMediaInfo
    Public WebClient As New Net.WebClient

    Public UiThread As Threading.Thread
    Public UiTaskScehule As TaskScheduler
    Public UiTaskfactory As TaskFactory = New TaskFactory(TaskScheduler.FromCurrentSynchronizationContext)

    Private OutputDevice As WaveOutEvent
    Private AudioFile As AudioFileReader

    Private Client As New YoutubeExplode.YoutubeClient

    <CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")>
    Public Event DisposingData(Control As Control)

    Public SongTitle As String
    Public SongArtist As String

    Public IsFromPlaylist As Boolean

    Private WithEvents WorkaroundTimer As New Timer With {.Interval = 100}

    Public CropAudio As Boolean = False
    Public CropStartSpan As TimeSpan
    Public CropEndSpan As TimeSpan

    Public UserPastedImage As Image

    Public BaseArtworkImage As Image

    Private AlbumDownloadThread As New Threading.Thread(AddressOf GetSpotifyAlbum)

    Public GeniusResult As GeniusResult

#Region "Thread Glicth Woraround"
    'Strange glitch from when a video was added from a playlist, whenever the thread would attempt to add a task to the UI Thread's Task factory,
    'The calling thread would immediatley exit with no error or stop code.
    'try catches yeiled no result
    Private Enum PipeType
        Setup = 0
        Progress = 1
        Close = 2
        idle = -1
        Finish = 4
        TriggerError = -10
        TriggerWarning = -9
    End Enum
    Dim TypePipe As PipeType = -1
    Public Sub WorkaroundTick() Handles WorkaroundTimer.Tick
        Select Case TypePipe
            Case PipeType.Progress
                PbProgress.PerformStep()
            Case PipeType.Finish
                PbProgress.Value = PbProgress.Maximum
                WorkaroundTimer.Stop()
            Case PipeType.TriggerError
                PbProgress.Value = PbProgress.Maximum
                WorkaroundTimer.Stop()
                ProgressbarModification.SetState(PbProgress, 2)
            Case PipeType.TriggerWarning
                PbProgress.Value = 3
                ProgressbarModification.SetState(PbProgress, 3)
        End Select
        TypePipe = -1
    End Sub
#End Region


    Public Sub New(Data As AudioControlData)
        InitializeComponent()
        StartInstance(Data)
    End Sub
    Public Sub StartInstance(Data As AudioControlData)
        PbProgress.Hide()
        Video = Data.Video
        SpotifyTrack = Data.SpotifyTrack
        MexData = Data.MexData
        IsFromPlaylist = Data.IsFromPlaylist
        lblYTtitle.Text = Data.Video.Title
        lblytChannel.Text = Data.Video.Author
        'PbIcon1.Image = My.Resources.YouTube
        Dim IconUrl As String = ""
        If IsNothing(Data.SpotifyTrack) Then
            BtnPlayAudio.Hide()
            LblArtist.Text = Data.MexData.Artist
            lblSpotifySong.Text = Data.MexData.Name
            SongArtist = Data.MexData.Artist
            SongTitle = Data.MexData.Name
            LblAlbum.Text = ""
            IconUrl = Data.Video.Thumbnails.MediumResUrl


            Console.WriteLine("Fetching genius lyrics")
            If TrackLogic.AttachLyrics Then
                GeniusResult = GetLyrics(MexData.Artist, MexData.Name, False)
            End If

        Else
            Console.WriteLine(Data.SpotifyTrack.Album.ReleaseDate)
            'Pbicon2.Image = My.Resources.Spotify
            LblAlbum.Text = "Album: " & Data.SpotifyTrack.Album.Name
            LblArtist.Text = "Artist: " & Data.SpotifyTrack.Artists(0).Name
            SongArtist = Data.SpotifyTrack.Artists(0).Name
            lblSpotifySong.Text = "Song: " & Data.SpotifyTrack.Name
            SongTitle = Data.SpotifyTrack.Name
            IconUrl = Data.SpotifyTrack.Album.Images(0).Url
            If Data.SpotifyTrack.PreviewUrl = "" Then
                BtnPlayAudio.Hide()
            End If
            RefreshSpotifyAlbum()

            Console.WriteLine("Fetching genius lyrics")
            If TrackLogic.AttachLyrics Then
                GeniusResult = GetLyrics(SpotifyTrack.Artists(0).Name, SpotifyTrack.Name, True)
            End If

        End If
        InitialiseIcons()
        If IconUrl = "" Then
            PbArtwork.Image = My.Resources.YouTube
        Else
            PbArtwork.Image = My.Resources.Loading1
            Dim Artworkdownloadthread As New Threading.Thread(Sub(Url As String)
                                                                  Dim MyClient As New Net.WebClient
                                                                  Dim resimage As Image = Image.FromStream(New IO.MemoryStream(MyClient.DownloadData(Url)))
                                                                  BaseArtworkImage = resimage
                                                                  PbArtwork.Image = resimage
                                                              End Sub)
            Artworkdownloadthread.Start(IconUrl)
        End If
    End Sub

    Public Sub InitialiseIcons()
        Console.WriteLine("Initialising icons")
        If FlowIcons.Controls.Count <> 0 Then
            FlowIcons.Controls.Clear()
        End If
        If Not IsNothing(Video) Then
            FlowIcons.Controls.Add(IconBuilder(My.Resources.YouTube, Video.GetUrl))
        End If
        If Not IsNothing(SpotifyTrack) Then
            If SpotifyTrack.HasError = False Then
                FlowIcons.Controls.Add(IconBuilder(My.Resources.Spotify, SpotifyTrack.Uri))
            End If
        End If
        If Not IsNothing(GeniusResult) Then
            If GeniusResult.Available Then
                FlowIcons.Controls.Add(IconBuilder(My.Resources.genius, GeniusResult.URL))
            End If
        End If
    End Sub
    Private Function IconBuilder(Icon As Image, Link As String) As PictureBox
        Dim PBI As New PictureBox With {
            .Image = Icon,
            .Size = New Size(20, 20),
            .SizeMode = PictureBoxSizeMode.Zoom}
        PBI.Tag = Link
        AddHandler PBI.DoubleClick, Sub(sender As Object, e As EventArgs)
                                        Dim Url As String = CType(sender, PictureBox).Tag
                                        If Not IsNothing(Url) Then
                                            If Url <> "" Then
                                                Process.Start(Url)
                                            End If
                                        End If
                                    End Sub
        Return PBI
    End Function

    Private Sub GetSpotifyAlbum()
        Console.WriteLine("getting album...")
        If Not IsNothing(SpotifyTrack) Then
            Console.WriteLine("Passed track check")
            If IsNothing(SpotifyAlbum) Then
                Console.WriteLine("Passed album check; Getting album")
                Dim alb As SpotifyAPI.Web.Models.FullAlbum = DownloaderInterface.MusicInterface.Spotify.Spotify.GetAlbum(SpotifyTrack.Album.Id)
                SpotifyAlbum = alb
            Else
                Console.WriteLine("Failed track check; Checking IDs...")
                If SpotifyTrack.Album.Id <> SpotifyAlbum.Id Then
                    Console.WriteLine("Passed ID check; Getting album")
                    Dim alb As SpotifyAPI.Web.Models.FullAlbum = DownloaderInterface.MusicInterface.Spotify.Spotify.GetAlbum(SpotifyTrack.Album.Id)
                    SpotifyAlbum = alb
                End If
            End If
        End If
    End Sub
    Public Sub RefreshSpotifyAlbum()
        If Not AlbumDownloadThread.ThreadState = Threading.ThreadState.Running Then
            Try
                AlbumDownloadThread.Start()
            Catch ex As Exception
            End Try
        End If
    End Sub
    Public Async Sub GetVideoBack()
        Video = Await Client.GetVideoAsync(Video.Id)
        PbArtwork.Show()
    End Sub
    Private Sub BtnPlayAudio_Click(sender As Object, e As EventArgs) Handles BtnPlayAudio.Click
        If BtnPlayAudio.Text = "Play" Then
            Dim DownloadThread As New Threading.Thread(AddressOf DownloadAudio)
            DownloadThread.Start(SpotifyTrack.PreviewUrl)
        ElseIf BtnPlayAudio.Text = "Stop" Then
            OutputDevice.Stop()
        End If
    End Sub
    Public Sub DownloadAudio(url As String)
        If url <> "" Then
            Dim address As Uri
            Dim filename As String = ""
            address = New Uri(url)
            filename = System.IO.Path.GetFileName(address.LocalPath)
            If Not IO.File.Exists("AudioCache\" & filename) Then
                BtnPlayAudio.Text = "Downloading..."
                IO.File.WriteAllBytes("AudioCache\" & filename, WebClient.DownloadData(url))
            End If
            If IsNothing(OutputDevice) Then
                OutputDevice = New WaveOutEvent()
                AddHandler OutputDevice.PlaybackStopped, Sub()
                                                             Try
                                                                 BtnPlayAudio.Text = "Play"
                                                                 AudioFile.Position = 0
                                                             Catch ex As Exception
                                                             End Try
                                                         End Sub
            End If
            If IsNothing(AudioFile) Then
                AudioFile = New AudioFileReader("AudioCache\" & filename)
                OutputDevice.Init(AudioFile)
            End If
            BtnPlayAudio.Text = "Stop"
            OutputDevice.Play()
        End If
    End Sub
    Public Sub DisposeData()
        Video = Nothing
        SpotifyTrack = Nothing
        MexData = Nothing
        If Not IsNothing(WebClient) Then
            WebClient.Dispose()
        End If
        If Not IsNothing(OutputDevice) Then
            OutputDevice.Dispose()
        End If
        If Not IsNothing(AudioFile) Then
            AudioFile.Dispose()
        End If
        If Not IsNothing(Me) Then
            Me.Dispose()
        End If
        RaiseEvent DisposingData(Me)
    End Sub
    Private Sub PbBtnClose_Click(sender As Object, e As EventArgs) Handles PbBtnClose.Click
        DisposeData()
    End Sub


    
    Public Sub InvokeTrackDownload()
        If Not Downloading Then
            Console.WriteLine("Invoker starting download")
            Dim DownloadThread As New Threading.Thread(AddressOf DownloadTrack)
            DownloadThread.Start()
        Else
            Console.WriteLine("Invokerfailed to start download: Already downloading")

        End If
    End Sub





    Private Event ShowDat()
    Public Async Sub SD() Handles Me.ShowDat
        Await Me.UiTaskfactory.StartNew(Sub()
                                            Downloading = True
                                            PbProgress.Maximum = 5
                                            PbProgress.Value = 0
                                            PbProgress.Step = 1
                                            PbProgress.Show()
                                        End Sub)

    End Sub

    Private Event Progress()
    Public Async Sub SPD() Handles Me.Progress
        Await Me.UiTaskfactory.StartNew(Sub()
                                            PbProgress.PerformStep()
                                        End Sub)
    End Sub
    Private Event Hideprog()
    Public Async Sub SPHD() Handles Me.Progress
        Await Me.UiTaskfactory.StartNew(Sub()
                                            PbProgress.Hide()
                                        End Sub)
    End Sub





    Private Async Sub DownloadTrack()
        Console.WriteLine("Starting download...")
        Downloading = True
        If Not IsFromPlaylist Then
            Await UiTaskfactory.StartNew(Sub()
                                             If CropAudio Then
                                                 PbProgress.Maximum = 6
                                             Else
                                                 PbProgress.Maximum = 5
                                             End If
                                             PbProgress.Value = 0
                                             PbProgress.Step = 1
                                             PbProgress.Show()
                                         End Sub)
        End If



        Dim VideoID As String = YoutubeExplode.YoutubeClient.ParseVideoId(Video.GetUrl)
        Console.WriteLine("Downloading video with id of {0}", VideoID)
        If Not IsFromPlaylist Then
            Await UiTaskfactory.StartNew(Sub()
                                             PbProgress.PerformStep()
                                         End Sub)
        Else
            TypePipe = PipeType.Progress
        End If

        Dim SteaminfoSet As MediaStreams.MediaStreamInfoSet = Await Client.GetVideoMediaStreamInfosAsync(VideoID)
        If Not IsFromPlaylist Then
            Await UiTaskfactory.StartNew(Sub()
                                             PbProgress.PerformStep()
                                         End Sub)
        Else
            TypePipe = PipeType.Progress
        End If


        Console.WriteLine("Streams fetched")


        Dim HighestQuality As Integer = 0
        For Each inf In SteaminfoSet.Audio
            If inf.Bitrate > HighestQuality Then
                HighestQuality = inf.Bitrate
            End If
        Next
        Dim HighestQualities As List(Of MediaStreams.AudioStreamInfo) = SteaminfoSet.Audio.Where(Function(x)
                                                                                                     If x.Bitrate >= HighestQuality Then
                                                                                                         Return True
                                                                                                     Else
                                                                                                         Return False
                                                                                                     End If
                                                                                                 End Function).ToList
        For Each HighQual In HighestQualities
            Console.WriteLine($"Highest Bitrate Stream, bitrate: {HighQual.Bitrate}, Encoding: {HighQual.AudioEncoding}")
        Next
        Dim auinfo = HighestQualities(0)

        Dim ext As String = "unknown"
        Select Case auinfo.AudioEncoding
            Case MediaStreams.AudioEncoding.Aac
                ext = "aac"
            Case MediaStreams.AudioEncoding.Opus
                ext = "opus"
            Case MediaStreams.AudioEncoding.Vorbis
                ext = "vorbis"
        End Select

        Console.WriteLine("ext: {0}", ext)
        Console.WriteLine("Downloading...")

        Dim Filename As String = ""
        If SongArtist = "" Then
            Filename = Video.Title
        Else
            Filename = SongArtist & " - " & SongTitle
        End If
        Filename = ScrubFilename(Filename)

        Try
            If IO.File.Exists("Downloads\" & Filename & "." & ext) Then
                IO.File.Delete("Downloads\" & Filename & "." & ext)
            End If
            If Not IO.File.Exists("Downloads\" & Filename & "." & ext) Then
                Console.WriteLine("Download Start.")
                Dim StarTT As Date = Now
                Await Client.DownloadMediaStreamAsync(auinfo, "Downloads\" & Filename & "." & ext)
                Dim endT As Date = Now
                Dim dif As TimeSpan = endT.Subtract(StarTT)
                Dim difs As Double = Math.Round(dif.TotalSeconds, 2)
                Console.WriteLine($"Download complete in {difs} Seconds.")
                If difs < 0.5 Then
                    Console.WriteLine("Something is wrong....")
                End If
            End If
        Catch ex As Exception
            Console.WriteLine(ex.Message)
        End Try
        If Not IsFromPlaylist Then
            Await UiTaskfactory.StartNew(Sub()
                                             PbProgress.PerformStep()
                                         End Sub)
        Else
            TypePipe = PipeType.Progress
        End If


        Dim DownloadTries As Integer = 0

RetryDownload:
        DownloadTries = DownloadTries + 1
        Dim ExitedWithError As Boolean = False
        Try



            Console.WriteLine($"File extension: {TrackLogic.Extension}")


            Dim Mp3Out As String = "Music\" & Filename & "." & TrackLogic.Extension
            If CropAudio Then
                Mp3Out = "audiocache\" & Filename & ".crop.mp3"
            End If


            If IO.File.Exists(Mp3Out) Then
                IO.File.Delete(Mp3Out)
            End If
            Console.WriteLine("Conversion Start")
            Dim AudioConversion As IConversion = Conversion.Convert("Downloads\" & Filename & "." & ext, Mp3Out)
            AudioConversion.SetAudioBitrate(auinfo.Bitrate)
            AudioConversion.UseHardwareAcceleration(Enums.HardwareAccelerator.Auto, Enums.VideoCodec.H264_cuvid, Enums.VideoCodec.H264_cuvid)
            Dim timer As New FunctionTimer
            Dim result As Model.IConversionResult = Await AudioConversion.Start()
            'Console.WriteLine($"   >>>>> Conversion complete, duration: {Math.Round(timer.GetDuration.TotalSeconds, 2)}")

            If Not IsFromPlaylist Then

                Await UiTaskfactory.StartNew(Sub()
                                                 PbProgress.PerformStep()
                                             End Sub)
            Else
                TypePipe = PipeType.Progress


            End If


            Console.WriteLine("   >>>Completed {0} secconds", Math.Round(result.Duration.TotalSeconds, 2))
            If result.Success Then
                Console.WriteLine("Conversion Successfull.")
            Else
                Console.WriteLine("Conversion Failiure.")
            End If
            Console.WriteLine("Conversion Finished  ")
            Dim DestFile As String = "Music\" & Filename & "." & TrackLogic.Extension

            If CropAudio Then
                Dim BaseMP3Out As String = "Audiocache\" & Filename & ".mp3"
                TrimMp3(Mp3Out, BaseMP3Out, CropStartSpan, CropEndSpan)
                If Not IsFromPlaylist Then
                    Await UiTaskfactory.StartNew(Sub()
                                                     PbProgress.PerformStep()
                                                 End Sub)
                Else
                    TypePipe = PipeType.Progress
                End If
                Mp3Out = BaseMP3Out
                Console.WriteLine("Audio Cropped")
                If TrackLogic.Extension.ToLower <> "mp3" Then
                    Dim SeccondaryAudioConversion As IConversion = Conversion.Convert(BaseMP3Out, DestFile)
                    SeccondaryAudioConversion.SetAudioBitrate(auinfo.Bitrate)
                    SeccondaryAudioConversion.UseHardwareAcceleration(Enums.HardwareAccelerator.Auto, Enums.VideoCodec.H264_cuvid, Enums.VideoCodec.H264_cuvid)
                    Dim Seccondaryresult As Model.IConversionResult = Await SeccondaryAudioConversion.Start()
                Else
                    IO.File.Move(BaseMP3Out, DestFile)
                End If
            End If



            Dim ID3File As TagLib.File = TagLib.File.Create(Mp3Out)
            TagLib.Id3v2.Tag.DefaultVersion = 3
            TagLib.Id3v2.Tag.ForceDefaultVersion = True
            ID3File.Tag.Title = SongTitle
            ID3File.Tag.AlbumArtists = {SongArtist}

            If TrackLogic.AttachLyrics Then
                If Not IsNothing(GeniusResult) Then
                    If GeniusResult.Available Then
                        ID3File.Tag.Lyrics = GeniusResult.Lyrics
                    End If
                End If
            End If


            If Not IsNothing(SpotifyTrack) Then
                ID3File.Tag.Album = SpotifyTrack.Album.Name
                ID3File.Tag.Year = SpotifyTrack.Album.ReleaseDate.Split("-")(0)
                Dim ImageFile As String = "ImageCache\" & Filename & ".jpeg"
                PbArtwork.Image.Save(ImageFile)
                Dim picture As TagLib.Picture = New TagLib.Picture(ImageFile)
                Dim albumCoverPictFrame As New TagLib.Id3v2.AttachedPictureFrame(picture)
                albumCoverPictFrame.MimeType = Net.Mime.MediaTypeNames.Image.Jpeg
                albumCoverPictFrame.Type = TagLib.PictureType.FrontCover
                Dim pictFrames() As TagLib.IPicture = {albumCoverPictFrame}
                ID3File.Tag.Pictures = pictFrames
                If Not IsNothing(SpotifyAlbum) Then
                    If Not SpotifyAlbum.HasError Then
                        Console.WriteLine("Embedding genre data...")
                        Console.WriteLine($"Genres: {String.Join(", ", SpotifyAlbum.Genres)}")
                        ID3File.Tag.Genres = SpotifyAlbum.Genres.ToArray
                        ID3File.Tag.TrackCount = SpotifyAlbum.TotalTracks
                        ID3File.Tag.Track = SpotifyAlbum.Tracks.Items.IndexOf(SpotifyAlbum.Tracks.Items.Where(Function(x)
                                                                                                                  If x.Id = SpotifyTrack.Id Then
                                                                                                                      Return True
                                                                                                                  Else
                                                                                                                      Return False
                                                                                                                  End If
                                                                                                              End Function)(0)) + 1
                        ID3File.Tag.Disc = SpotifyTrack.DiscNumber
                        ID3File.Tag.AlbumSort = SpotifyAlbum.AlbumType
                    End If
                End If
            Else

                If Not IsNothing(UserPastedImage) Then

                    Dim SavedName As String = $"ImageCache\{Video.Id}pasted_artwork.jpg"

                    If IO.File.Exists(SavedName) Then
                        IO.File.Delete(SavedName)
                    End If
                    UserPastedImage.Save(SavedName)
                    Dim picture As TagLib.Picture = New TagLib.Picture(SavedName)
                    Dim albumCoverPictFrame As New TagLib.Id3v2.AttachedPictureFrame(picture)
                    albumCoverPictFrame.MimeType = Net.Mime.MediaTypeNames.Image.Jpeg
                    albumCoverPictFrame.Type = TagLib.PictureType.FrontCover
                    Dim pictFrames() As TagLib.IPicture = {albumCoverPictFrame}
                    ID3File.Tag.Pictures = pictFrames
                End If


            End If

            ID3File.Save()

            If Not IsFromPlaylist Then
                Await UiTaskfactory.StartNew(Sub()
                                                 PbProgress.PerformStep()
                                             End Sub)
                Threading.Thread.Sleep(500)
                Await UiTaskfactory.StartNew(Sub()
                                                 PbProgress.Hide()
                                             End Sub)
            Else
                TypePipe = PipeType.Finish
            End If
        Catch ex As Exception
            Console.WriteLine(ex.Message)
            Console.WriteLine(ex.StackTrace)
            Console.WriteLine(ex.Source)
            ExitedWithError = True
        End Try
        If ExitedWithError Then
            If DownloadTries >= TrackLogic.MaxDownloadRetries Then
                If IsFromPlaylist Then
                    TypePipe = PipeType.TriggerError
                Else
                    Await UiTaskfactory.StartNew(Sub()
                                                     PbProgress.Value = PbProgress.Maximum
                                                     WorkaroundTimer.Stop()
                                                     ProgressbarModification.SetState(PbProgress, 2)
                                                 End Sub)
                End If
            Else
                If IsFromPlaylist Then
                    TypePipe = PipeType.TriggerWarning
                Else
                    Await UiTaskfactory.StartNew(Sub()
                                                     PbProgress.Value = 3
                                                     ProgressbarModification.SetState(PbProgress, 3)
                                                 End Sub)
                End If
                Console.WriteLine("AN ERROR OCCOURED! RETRYING DOWNLOAD.")
                Threading.Thread.Sleep(2000)
                GoTo RetryDownload
            End If
        End If
        Downloading = False
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles btnDownload.Click
        If Not Downloading Then
            If IsFromPlaylist Then
                If CropAudio Then
                    PbProgress.Maximum = 6
                Else
                    PbProgress.Maximum = 5
                End If
                PbProgress.Value = 0
                PbProgress.Step = 1
                ProgressbarModification.SetState(PbProgress, 1)
                PbProgress.Show()
                WorkaroundTimer.Start()
            End If
            Dim DownloadThread As New Threading.Thread(AddressOf DownloadTrack)
            DownloadThread.Start()
        End If
    End Sub


    Public Sub WorkerComplete() Handles BackgroundDownloader.RunWorkerCompleted
        PbProgress.Hide()
    End Sub
    Public Sub Workerprogress() Handles BackgroundDownloader.ProgressChanged
        PbProgress.PerformStep()
    End Sub

    Public Function ScrubFilename(Filename As String)
        Return Filename.Replace("/", "").Replace("\", "").Replace("|", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace(":", "").Replace("""", "").Replace("*", "")
    End Function



    Private Sub TrimMp3(ByVal inputPath As String, ByVal outputPath As String, ByVal begin As TimeSpan?, ByVal [end] As TimeSpan?)
        Console.WriteLine($"Crop Start Time: {begin.Value.TotalSeconds} end: {[end].Value.TotalSeconds}")


        If begin.HasValue AndAlso [end].HasValue AndAlso begin > [end] Then Throw New ArgumentOutOfRangeException("end", "end should be greater than begin")
        Using reader = New Mp3FileReader(inputPath)
            Using writer = IO.File.Create(outputPath)
                Dim frame As Mp3Frame = Nothing
                While (AssignFunc(frame, reader.ReadNextFrame())) IsNot Nothing
                    If reader.CurrentTime >= begin OrElse Not begin.HasValue Then

                        If reader.CurrentTime <= [end] OrElse Not [end].HasValue Then
                            writer.Write(frame.RawData, 0, frame.RawData.Length)
                        Else
                            Exit While
                        End If
                    End If
                End While
            End Using
        End Using
    End Sub

    Shared Function AssignFunc(Of T)(ByRef target As T, value As T) As T
        target = value
        Return value
    End Function

    Private Sub PbCrop_Click(sender As Object, e As EventArgs) Handles PbCrop.Click
        If Downloading Then
            MessageBox.Show(Me, "This action is not permitted during track download.", "Track Data Input")
        Else
            Dim CropShow As New AudioTrimDialog(Video.Duration.TotalSeconds)
            AddHandler CropShow.DataSubmitted, Sub(St As TimeSpan, Et As TimeSpan)
                                                   CropStartSpan = St
                                                   CropEndSpan = Et
                                                   CropAudio = True
                                                   RefreshSpotifyData()
                                               End Sub
            CropShow.ShowDialog()
        End If
    End Sub
    Public Sub RefreshSpotifyData()
        If Not Downloading Then
            Console.WriteLine("Restarting Instance...")
            Dim diff As TimeSpan = CropEndSpan.Subtract(CropStartSpan)
            Dim usedif As Boolean = False
            If Not IsNothing(diff) Then
                usedif = diff.TotalMilliseconds > 0
            End If
            Dim SpotifyResult As SpotifyAPI.Web.Models.FullTrack = DownloaderInterface.MusicInterface.Spotify.GetSpotifyTrack(Video, MexData, diff, usedif)
            Dim ControlData As New AudioControlData(Video, SpotifyResult, MexData, IsFromPlaylist)
            StartInstance(ControlData)
        End If
    End Sub
    Private Sub EditTrackData() Handles lblSpotifySong.DoubleClick, LblArtist.DoubleClick, PbBtnEditMex.Click
        Dim PreMex As MexMediaInfo = MexData
        Dim mexdialog As New ManualTrackInputDialog(MexData.Name, MexData.Artist)
        AddHandler mexdialog.DataSubmitted, Sub(Track As String, artist As String)
                                                Dim NewMex As New MexMediaInfo(artist, Track)
                                                MexData = NewMex
                                                RefreshSpotifyData()
                                            End Sub
        mexdialog.ShowDialog()
    End Sub


    Private Sub LblAlbum_Click(sender As Object, e As EventArgs) Handles LblAlbum.Click

    End Sub

    Private Sub CMSArtwork_Opening(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles CMSArtwork.Opening
        If IsNothing(UserPastedImage) Then
            TSMIRemoveArtwork.Enabled = False
        Else
            TSMIRemoveArtwork.Enabled = True
        End If
        If My.Computer.Clipboard.ContainsImage Then
            TSMIPasteArtwork.Enabled = True
        Else
            TSMIPasteArtwork.Enabled = False
        End If
    End Sub

    Private Sub TSMIPasteArtwork_Click(sender As Object, e As EventArgs) Handles TSMIPasteArtwork.Click
        If Clipboard.ContainsImage Then
            Dim PastedImage As Image = Clipboard.GetImage()
            If PastedImage.Width = PastedImage.Height Then
                If PastedImage.Width > 1000 Then
                    Console.WriteLine("Resizeing Image...")
                    Dim FixedImage As New Bitmap(1000, 1000)
                    Dim G As Graphics = Graphics.FromImage(FixedImage)
                    G.CompositingQuality = Drawing2D.CompositingQuality.HighQuality
                    G.DrawImage(PastedImage, New RectangleF(0, 0, 1000, 100))
                    G.Save()
                    PastedImage = FixedImage
                    Console.WriteLine("Image Resize Complete.")
                End If
            End If

            PbArtwork.Image = PastedImage
            UserPastedImage = PastedImage
        End If
    End Sub

    Private Sub TSMIRemoveArtwork_Click(sender As Object, e As EventArgs) Handles TSMIRemoveArtwork.Click
        UserPastedImage = Nothing
        PbArtwork.Image = BaseArtworkImage

    End Sub
    Public Class FunctionTimer
        Dim startt As Date
        Sub New()
            startt = Now
        End Sub
        Public Function GetDuration() As TimeSpan
            Dim endT As Date = Now
            Dim dif As TimeSpan = endT.Subtract(startt)
            Return dif
        End Function
    End Class
End Class
Public Class AudioControlData
    Public Video As YoutubeExplode.Models.Video
    Public SpotifyTrack As SpotifyAPI.Web.Models.FullTrack
    Public MexData As MexMediaInfo
    Public IsFromPlaylist As Boolean = False
    Public Sub New(Vid As YoutubeExplode.Models.Video, Track As SpotifyAPI.Web.Models.FullTrack, TitleData As MexMediaInfo, Optional Playlist As Boolean = False)
        Video = Vid
        SpotifyTrack = Track
        MexData = TitleData
        IsFromPlaylist = Playlist
    End Sub
End Class
