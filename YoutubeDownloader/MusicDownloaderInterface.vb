﻿Imports YoutubeExplode
Imports YoutubeExplode.Models
Imports SpotifyAPI.Web
Imports Xabe.FFmpeg
Public Class MusicDownloaderinterface
    Dim Youtube As New YoutubeClient
    Dim Webclient As New Net.WebClient
    Public SpotifyNotAvalableException As New Exception
    Public Spotify As SpotifyApiBridge
    Public Event PlaylistLoadComplete()
    Public Event LoadControls(Controls As Control)
    Public UiThread As Threading.Thread
    Public UiTaskScehule As TaskScheduler
    Public UiTaskfactory As TaskFactory
    Private Sub BtnGo_Click(sender As Object, e As EventArgs) Handles BtnGo.Click
        ParseEntryText(txturl.Text)
    End Sub

    Public Sub ParseEntryText(Txt As String)
        If IsUrl(Txt) Then
            'url
            Dim url As String = Txt
            If url.ToLower.StartsWith("https://www.youtube.com/playlist?") Then
                Dim playlistid As String = Txt.Remove(0, "https://www.youtube.com/playlist?list=".Length)
                FetchVideosFromPlaylist(playlistid)
            ElseIf url.ToLower.Contains("&list=") Then

                Dim urlparts As List(Of String) = url.Split("&").ToList
                Dim listid As String = ""
                For Each part In urlparts
                    If part.ToLower.StartsWith("list=") Then
                        listid = part.Remove(0, "list=".Length)
                    End If
                Next

                Dim res As DialogResult = MessageBox.Show(Me, "The video you have entered is part of a playlist. Would you like to load the entire playlist?", "Playlist", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)

                If res = DialogResult.Yes Then
                    'load playlist
                    FetchVideosFromPlaylist(listid)
                ElseIf res = DialogResult.No Then
                    FetchVideoFromUrl(Txt)
                End If
            Else


                FetchVideoFromUrl(Txt)

            End If


        Else
            'term
            FetchVideoFromTerm(Txt)
        End If
    End Sub


    Private Sub Flow_DragEnter(ByVal sender As Object, ByVal e As System.Windows.Forms.DragEventArgs) Handles FlowItems.DragEnter
        If (e.Data.GetDataPresent(DataFormats.Text)) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub
    Private Sub Flow_DragDrop(ByVal sender As Object, ByVal e As System.Windows.Forms.DragEventArgs) Handles FlowItems.DragDrop
        ParseEntryText(e.Data.GetData(DataFormats.Text).ToString)
    End Sub

    Public Sub Main() Handles MyBase.Load
        CheckForIllegalCrossThreadCalls = False
        UiThread = Threading.Thread.CurrentThread
        UiTaskScehule = TaskScheduler.FromCurrentSynchronizationContext
        UiTaskfactory = New TaskFactory(UiTaskScehule)
        Console.WriteLine("loading...")
        Console.WriteLine("ID: {0}", SpotifyData.ClientID)
        Console.WriteLine("sec: {0}", SpotifyData.ClientSecret)
        If SpotifyData.ClientID <> "" Then
            If SpotifyData.ClientSecret <> "" Then
                Spotify = New SpotifyApiBridge(SpotifyData.ClientID, SpotifyData.ClientSecret)
            End If
        End If
        Console.WriteLine("finished.")
        LoadUIElements()
        Me.SetStyle(ControlStyles.AllPaintingInWmPaint, True)
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer, True)
        Me.SetStyle(ControlStyles.Selectable, True)
        Me.SetStyle(ControlStyles.UserPaint, True)
        Me.DoubleBuffered = True
    End Sub

    Public Sub LoadUIElements()
        Me.BackgroundImage = My.Resources.GreyBacker1
        FlowItems.BackgroundImage = My.Resources.GreyBacker1


    End Sub




    Public Sub InvalidateOnScrol() Handles FlowItems.Scroll
        Application.DoEvents()
    End Sub
    Protected Overrides Sub OnScroll(ByVal se As ScrollEventArgs)
        Me.Invalidate()
        MyBase.OnScroll(se)
    End Sub






    Public Enum QueryType
        Unknown = 0
        Url = 1
        SearchTerm = 2
    End Enum


    Async Sub FetchVideoFromUrl(url As String)
        Dim Vid As Video = Await GetYoutubeVideo(url)
        Dim SpotifyResult As Models.FullTrack = Spotify.GetSpotifyTrack(Vid)
        Dim MexData As MexMediaInfo = MexMediaInfo.FromMediaTitle(Vid.Title)
        Dim UiControlData As New AudioControlData(Vid, SpotifyResult, MexData)
        Dim UiControl As New AudioEntry(UiControlData)
        AddHandler UiControl.DisposingData, Sub(x As Control)
                                                FlowItems.Controls.Remove(x)
                                            End Sub
        FlowItems.Controls.Add(UiControl)
    End Sub



    Async Sub FetchVideosFromPlaylist(PlaylistID As String)
        Dim Playlist As Playlist = Await Youtube.GetPlaylistAsync(PlaylistID)
        Dim BackgroundThread As New Threading.Thread(AddressOf BackgroundPlaylistDownload)
        BackgroundThread.Start(Playlist)
        txturl.Enabled = False
        BtnGo.Enabled = False
    End Sub
    Public Sub ReEnableUrlFeed() Handles Me.PlaylistLoadComplete
        txturl.Enabled = True
        BtnGo.Enabled = True
    End Sub


    Public Sub BackgroundPlaylistDownload(Playlist As Playlist)
        For Each video In Playlist.Videos
            Dim SpotifyResult As Models.FullTrack = Spotify.GetSpotifyTrack(video)
            Dim MexData As MexMediaInfo = MexMediaInfo.FromMediaTitle(video.Title)
            Dim UiControlData As New AudioControlData(video, SpotifyResult, MexData, True)
            Dim UiControl As New AudioEntry(UiControlData)
            AddHandler UiControl.DisposingData, Sub(x As Control)
                                                    FlowItems.Controls.Remove(x)
                                                End Sub
            UiTaskfactory.StartNew(Sub()
                                       FlowItems.Controls.Add(UiControl)
                                   End Sub)
        Next
        RaiseEvent PlaylistLoadComplete()
    End Sub

    Async Sub FetchVideoFromTerm(Term As String)
        Dim Vid As Video = Await SearchVideos(Term)
        Dim SpotifyResult As Models.FullTrack = Spotify.GetSpotifyTrack(Vid)
        Dim MexData As MexMediaInfo = MexMediaInfo.FromMediaTitle(Vid.Title)
        Dim UiControlData As New AudioControlData(Vid, SpotifyResult, MexData)
        Dim UiControl As New AudioEntry(UiControlData)
        AddHandler UiControl.DisposingData, Sub(x As Control)
                                                FlowItems.Controls.Remove(x)
                                            End Sub
        FlowItems.Controls.Add(UiControl)
    End Sub

    Public Async Function GetYoutubeVideo(Url As String) As Task(Of Video)
        Dim VideoID As String = YoutubeClient.ParseVideoId(Url)
        Dim Video As Video = Await Youtube.GetVideoAsync(VideoID)
        Return Video
    End Function


    Public Async Function SearchVideos(Term As String) As Task(Of Video)
        Dim Results As IReadOnlyList(Of Video) = Await Youtube.SearchVideosAsync(Term, 1)
        If Results.Count <> 0 Then
            Return (Results(0))
        Else
            Return Nothing
        End If
    End Function

    Public Function IsUrl(Term As String) As Boolean
        If Term.ToLower.StartsWith("www.") Then
            Term = "http://" & Term
        End If
        Return Uri.IsWellFormedUriString(Term, UriKind.Absolute)
    End Function

    Private Sub TxtUrlEnter(sender As Object, e As KeyEventArgs) Handles txturl.KeyDown
        If e.KeyData = Keys.Return Then
            BtnGo.PerformClick()
        End If
    End Sub

    Private Sub GroupBox1_Enter(sender As Object, e As EventArgs) Handles GroupBox1.Enter

    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Dim res As DialogResult = MessageBox.Show(Me, "By clearing the current list, you will loose all current progress. Proceed?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2)
        If res = DialogResult.Yes Then
            Do Until FlowItems.Controls.OfType(Of AudioEntry).Count = 0
                For Each control In FlowItems.Controls.OfType(Of AudioEntry)
                    control.DisposeData()
                Next
            Loop
        End If
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles BtnDownloadAll.Click
        Dim startt As New Threading.Thread(Sub()
                                               For Each item In FlowItems.Controls.OfType(Of AudioEntry)
                                                   Threading.Thread.Sleep(800)
                                                   item.btnDownload.PerformClick()
                                               Next
                                           End Sub)
        startt.Start()


    End Sub

    Private Sub PbSettings_Click(sender As Object, e As EventArgs)
        SettingsMenu.Show()
        SettingsMenu.BringToFront()
    End Sub

    Private Sub PbOpenOutput_Click(sender As Object, e As EventArgs) Handles PbOpenOutput.Click
        Dim resp As String = IO.Directory.GetCurrentDirectory
        If Not resp.EndsWith("\") Then
            resp = resp & "\"
        End If
        Process.Start(resp & "Music")
    End Sub

    Private Sub PbBtnBack_Click(sender As Object, e As EventArgs) Handles PbBtnBack.Click
        DownloaderInterface.SetInterface(DownloaderInterface.InterfaceScreen.MainInterface)
    End Sub

    Private Sub txturl_TextChanged(sender As Object, e As EventArgs) Handles txturl.TextChanged

    End Sub
End Class
Public Class IniReader
    Public FileKeys As New List(Of KeyValuePair(Of String, String))
    Public Sub New(Inifile As String)
        For Each line In IO.File.ReadAllLines(Inifile)
            If Not line = "" And Not line.StartsWith("#") Then
                If line.Contains("=") Then
                    Dim arg1 As String = line.Split("=")(0)
                    Dim arg2 As String = line.Remove(0, arg1.Length + 1)
                    FileKeys.Add(New KeyValuePair(Of String, String)(arg1, arg2))
                Else
                    FileKeys.Add(New KeyValuePair(Of String, String)(line, ""))
                End If
            End If
        Next
    End Sub
    Public Function GetValue(Key As String) As String
        Dim ret As String = Nothing
        For Each entry In FileKeys
            If entry.Key.ToLower = Key.ToLower Then
                ret = entry.Value
            End If
        Next
        Return ret
    End Function
    Public Function FileContainsKey(Key As String) As Boolean
        Dim ret As String = Nothing
        For Each entry In FileKeys
            If entry.Key.ToLower = Key.ToLower Then
                ret = entry.Value
            End If
        Next
        Return Not IsNothing(ret)
    End Function
End Class