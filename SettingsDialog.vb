Imports System.Drawing
Imports System.Threading

Partial Public Class Plugin
    Private NotInheritable Class SettingsDialog
        Inherits System.Windows.Forms.Form
        Private isLoadComplete As Boolean = False
        Private isDirty As Boolean = False
        Private lastProfileIndex As Integer = -1

        Public Sub New()
            Try
                InitializeComponent()

                AddHandler activeHqpProfiles.SelectedIndexChanged, AddressOf activeHqpProfiles_SelectedIndexChanged
                AddHandler logDebugInfo.CheckedChanged, AddressOf logDebugInfo_CheckedChanged

                Me.Font = mbApiInterface.Setting_GetDefaultFont()
                Dim boldFont As New Font(Me.Font, FontStyle.Bold)

                Me.ipAddress.Items.Add("Automatic")
                Me.ipAddress.SelectedIndex = 0
                For index As Integer = 0 To hostAddresses.Length - 1
                    Dim address As String = hostAddresses(index).ToString()
                    Me.ipAddress.Items.Add(address)
                    If address = Settings.IpAddress Then
                        Me.ipAddress.SelectedIndex = index + 1
                    End If
                Next index
                If Not String.IsNullOrEmpty(Settings.IpAddress) AndAlso Me.ipAddress.SelectedIndex = 0 Then
                    Me.ipAddress.Items.Add(Settings.IpAddress)
                    Me.ipAddress.SelectedIndex = Me.ipAddress.Items.Count - 1
                End If
                Me.ipAddress.MaxDropDownItems = Me.ipAddress.Items.Count
                Me.port.Text = Settings.ServerPort.ToString()
                Me.activeHqpProfiles.BeginUpdate()
                For index As Integer = 0 To Settings.HQPProfiles.Count - 1
                    Me.activeHqpProfiles.Items.Add(Settings.HQPProfiles(index))
                Next index
                Me.activeHqpProfiles.SelectedIndex = Settings.DefaultProfileIndex
                Me.activeHqpProfiles.EndUpdate()

                Me.logDebugInfo.Checked = Settings.LogDebugInfo
                Me.viewButton.Visible = Settings.LogDebugInfo
                Me.viewButton.Left = Me.logDebugInfo.Right + 5
                isLoadComplete = True
                AddHandler addProfileButton.Click, AddressOf addProfileButton_Click
                AddHandler removeProfileButton.Click, AddressOf removeProfileButton_Click
                AddHandler testProfileButton.Click, AddressOf testProfileButton_Click

                AddHandler profileName.TextChanged, AddressOf profileChanged
                AddHandler ipAddress.TextChanged, AddressOf profileChanged
                AddHandler port.TextChanged, AddressOf profileChanged

                AddHandler viewButton.Click, AddressOf viewButton_Click
                AddHandler saveButton.Click, AddressOf saveButton_Click
                AddHandler closeButton.Click, AddressOf closeButton_Click

                AddHandler helpLabel.Click, AddressOf HelpLabel_Click
            Catch ex As Exception
                LogError(ex, "SettingDialog-New", ex.StackTrace)
                Throw ex
            End Try

        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            MyBase.Dispose(disposing)
        End Sub

        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            If isDirty Then
                Select Case MessageBox.Show(Me, "One or more values have been amended - do you want to save the changes?", "MusicBee HQPlayer Plugin", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                    Case DialogResult.Cancel
                        e.Cancel = True
                    Case Windows.Forms.DialogResult.OK
                        SaveSettings()
                End Select
            End If
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            If Not ipOverrideAddressMatched Then
                MessageBox.Show(Me, "WARNING: The selected IP Address " & Settings.IpAddress & " is not currently operational", "MusicBee HQPlayer Plugin")
            End If
        End Sub

        Private Sub logDebugInfo_CheckedChanged(sender As Object, e As EventArgs)
            Me.viewButton.Visible = Me.logDebugInfo.Checked
        End Sub

        Private Sub activeHqpProfiles_SelectedIndexChanged(sender As Object, e As EventArgs)
            Dim profile As HQPProfile
            If lastProfileIndex <> -1 Then
                RemoveHandler activeHqpProfiles.SelectedIndexChanged, AddressOf activeHqpProfiles_SelectedIndexChanged
                profile = UpdateCurrentHqpProfile()
                Me.activeHqpProfiles.Items(lastProfileIndex) = profile
                AddHandler activeHqpProfiles.SelectedIndexChanged, AddressOf activeHqpProfiles_SelectedIndexChanged
            End If
            lastProfileIndex = Me.activeHqpProfiles.SelectedIndex
            If lastProfileIndex <> -1 Then
                profile = DirectCast(Me.activeHqpProfiles.SelectedItem, HQPProfile)
                LoadProfile(profile)
                Me.profileName.Enabled = (lastProfileIndex > 0)
                Me.hqpIp.Enabled = (lastProfileIndex > 0)
            End If
            Me.removeProfileButton.Enabled = (lastProfileIndex > 0)
        End Sub

        Private Sub LoadProfile(profile As HQPProfile)
            Me.profileName.Text = profile.ProfileName
            Me.hqpIp.Text = profile.IpAddress
            Me.hqpVersion.Text = profile.product
            Dim value As String = ""
        End Sub

        Private Sub profileChanged(sender As Object, e As EventArgs)
            isDirty = True
        End Sub

        Private Sub addProfileButton_Click(sender As Object, e As EventArgs)
            Dim profile As New HQPProfile("HQPlayer(New" & activeHqpProfiles.Items.Count & ")")
            'Settings.HQPProfiles.Add(profile)
            Me.activeHqpProfiles.Items.Add(profile)
            Me.activeHqpProfiles.SelectedIndex = Me.activeHqpProfiles.Items.Count - 1
            Me.hqpIp.Text = ""
            isDirty = True
        End Sub

        Private Sub removeProfileButton_Click(sender As Object, e As EventArgs)
            Dim index As Integer = Me.activeHqpProfiles.SelectedIndex
            If index > 0 Then
                Me.activeHqpProfiles.SelectedIndex = index - 1
                'Settings.HQPProfiles.RemoveAt(index)
                Me.activeHqpProfiles.Items.RemoveAt(index)
                isDirty = True
            End If
        End Sub

        Private Sub testProfileButton_Click(sender As Object, e As EventArgs)
            Dim index As Integer = Me.activeHqpProfiles.SelectedIndex
            Try
                'UpdateCurrentHqpProfile()
                Dim profile As HQPProfile = DirectCast(Me.activeHqpProfiles.SelectedItem, HQPProfile)
                profile.ProfileName = Me.profileName.Text
                profile.IpAddress = Me.hqpIp.Text
                If (profile.ProfileName Is Nothing OrElse profile.ProfileName.Length = 0) Then
                    MessageBox.Show(Me, "HQPlayer device name cannot be blank.", "MusicBee HQPlayer Plugin")
                    Exit Sub
                End If

                If (Not isValidIpAddress(profile.IpAddress)) Then
                    MessageBox.Show(Me, "HQPlayer Ip address is invalid.", "MusicBee HQPlayer Plugin")
                    Exit Sub
                End If

                Dim hqpInterface As HQPInterface = New HQPInterface(profile)
                Try
                    Dim hqpMsg As String = hqpInterface.HQP_GetInfo(True)
                    profile = hqpInterface.ParseXML(hqpMsg)
                    'Me.hqpVersion.Text = profile.product & "," & profile.version & "," & profile.platform
                    Me.hqpVersion.Text = profile.product
                    MessageBox.Show(Me, "Successfuly connected to '" & profile.product & "' at " & profile.IpAddress, "MusicBee HQPlayer Plugin")
                Catch ex As Exception
                    Throw ex
                Finally
                    hqpInterface.Dispose()
                End Try
            Catch ex As Exception
                MessageBox.Show(Me, "Cannot connect to HQPlayer, please make sure HQPlayer is running.", "MusicBee HQPlayer Plugin")
            End Try
        End Sub

        Private Sub viewButton_Click(sender As Object, e As EventArgs)
            Process.Start("notepad.exe", """" & Settings.Logfilename & """")
        End Sub

        Private Sub closeButton_Click(sender As Object, e As EventArgs)
            If isDirty Then
                Select Case MessageBox.Show(Me, "One or more values have been amended - do you want to save the changes?", "MusicBee HQPlayer Plugin", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                    Case DialogResult.Cancel
                        Exit Sub
                    Case Windows.Forms.DialogResult.OK
                        isDirty = False
                        Me.Close()
                    Case Windows.Forms.DialogResult.No
                        isDirty = False
                        Me.Close()
                End Select
            Else
                Me.Close()
            End If
        End Sub

        Private Sub helpLabel_Click(sender As Object, e As EventArgs)
            Dim url As String = "www.github.com/tracemouse/Musicbee-HQPlayer/"
            Process.Start(url)
        End Sub

        Private Sub saveButton_Click(sender As Object, e As EventArgs)
            isDirty = False
            If SaveSettings() Then
                Me.Close()
            End If
        End Sub

        Private Function SaveSettings() As Boolean
            Me.Enabled = False
            Me.Cursor = Cursors.WaitCursor
            Me.Update()
            Try
                UpdateCurrentHqpProfile()

                For index As Integer = 0 To Me.activeHqpProfiles.Items.Count - 1
                    Dim profile As HQPProfile = DirectCast(Me.activeHqpProfiles.Items(index), HQPProfile)
                    If (profile.ProfileName Is Nothing OrElse profile.ProfileName.Length = 0) Then
                        Me.activeHqpProfiles.SelectedIndex = index
                        MessageBox.Show(Me, "HQPlayer device name cannot be blank.", "MusicBee HQPlayer Plugin")
                        Return False
                    End If

                    If (Not isValidIpAddress(profile.IpAddress)) Then
                        Me.activeHqpProfiles.SelectedIndex = index
                        MessageBox.Show(Me, "HQPlayer Ip address is invalid.", "MusicBee HQPlayer Plugin")
                        Return False
                    End If
                Next

                Settings.HQPProfiles.Clear()
                For index As Integer = 0 To Me.activeHqpProfiles.Items.Count - 1
                    Dim profile As HQPProfile = DirectCast(Me.activeHqpProfiles.Items(index), HQPProfile)
                    Settings.HQPProfiles.Add(profile)
                Next

                Settings.IpAddress = If(Me.ipAddress.SelectedIndex <= 0, "", Me.ipAddress.SelectedItem.ToString())
                If Not Integer.TryParse(Me.port.Text, Settings.ServerPort) Then
                    Settings.ServerPort = 16888
                End If
                Settings.DefaultProfileIndex = Me.activeHqpProfiles.SelectedIndex


                Settings.LogDebugInfo = Me.logDebugInfo.Checked
                Settings.SaveSettings()

                Dim restartThread As New Thread(AddressOf RestartServer)
                restartThread.IsBackground = True
                restartThread.Start()
                Threading.Thread.Sleep(200)
            Finally
                Me.Enabled = True
                Me.Cursor = Cursors.Default
            End Try
            Return True
        End Function

        Private Sub RestartServer()
            Try
                controller.Restart()
            Catch ex As Exception
                LogError(ex, "RestartController")
            End Try
            Try
                server.Restart(True)
            Catch ex As Exception
                LogError(ex, "RestartServer")
            End Try
        End Sub

        Private Function UpdateCurrentHqpProfile() As HQPProfile
            Dim profile As HQPProfile = DirectCast(Me.activeHqpProfiles.Items(lastProfileIndex), HQPProfile)
            profile.ProfileName = Me.profileName.Text
            profile.IpAddress = Me.hqpIp.Text
            Return profile
        End Function

        Private Function isValidIpAddress(ByVal strIP As String) As Boolean
            If (strIP Is Nothing OrElse strIP.Length = 0) Then
                Return False
            End If
            '检查IP地址是否合法函数
            Dim intLoop As Integer
            Dim arrIP() As String
            Dim isValid As Boolean = True
            arrIP = Split(strIP, ".") '将输入的IP用"."分割为数组，数组下标从0开始，所以有效IP分割后的数组上界必须为3  

            If UBound(arrIP) <> 3 Then
                isValid = False
            Else
                For intLoop = 0 To UBound(arrIP)
                    If Not IsNumeric(arrIP(intLoop)) Then       '检查数组元素中各项是否为数字，如果不是则不是有效IP  
                        isValid = False
                    Else
                        If CInt(arrIP(intLoop)) > 255 Or CInt(arrIP(intLoop)) < 0 Then       '检查IP数字是否满足IP的取值范围  
                            isValid = False
                        End If
                    End If
                Next
            End If
            Return isValid
        End Function

        Private Sub InitializeComponent()
            Me.logDebugInfo = New System.Windows.Forms.CheckBox()
            Me.hqpProfilesPrompt = New System.Windows.Forms.Label()
            Me.hqpIpPrompt = New System.Windows.Forms.Label()
            Me.hqpIp = New System.Windows.Forms.TextBox()
            Me.profileName = New System.Windows.Forms.TextBox()
            Me.profileNamePrompt = New System.Windows.Forms.Label()
            Me.portPrompt = New System.Windows.Forms.Label()
            Me.port = New System.Windows.Forms.TextBox()
            Me.closeButton = New System.Windows.Forms.Button()
            Me.saveButton = New System.Windows.Forms.Button()
            Me.activeHqpProfiles = New System.Windows.Forms.ListBox()
            Me.addProfileButton = New System.Windows.Forms.Button()
            Me.removeProfileButton = New System.Windows.Forms.Button()
            Me.viewButton = New System.Windows.Forms.Button()
            Me.ipAddressPrompt = New System.Windows.Forms.Label()
            Me.ipAddress = New System.Windows.Forms.ComboBox()
            Me.hqpVersionPrompt = New System.Windows.Forms.Label()
            Me.hqpVersion = New System.Windows.Forms.TextBox()
            Me.testProfileButton = New System.Windows.Forms.Button()
            Me.helpLabel = New System.Windows.Forms.LinkLabel()
            Me.SuspendLayout()
            '
            'help label
            '
            Me.helpLabel.AutoSize = True
            Me.helpLabel.Location = New System.Drawing.Point(490, 20)
            Me.helpLabel.Name = "helpLabel"
            Me.helpLabel.Size = New System.Drawing.Size(30, 13)
            Me.helpLabel.TabIndex = 0
            Me.helpLabel.Text = "Help"
            '
            'ipAddressPrompt
            '
            Me.ipAddressPrompt.AutoSize = True
            Me.ipAddressPrompt.Location = New System.Drawing.Point(35, 23)
            Me.ipAddressPrompt.Name = "ipAddressPrompt"
            Me.ipAddressPrompt.Size = New System.Drawing.Size(60, 13)
            Me.ipAddressPrompt.TabIndex = 0
            Me.ipAddressPrompt.Text = "IP address:"
            '
            'ipAddress
            '
            Me.ipAddress.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
            Me.ipAddress.FormattingEnabled = True
            Me.ipAddress.Location = New System.Drawing.Point(117, 20)
            Me.ipAddress.Name = "ipAddress"
            Me.ipAddress.Size = New System.Drawing.Size(170, 21)
            Me.ipAddress.TabIndex = 21

            'portPrompt
            '
            Me.portPrompt.AutoSize = True
            Me.portPrompt.Location = New System.Drawing.Point(340, 23)
            Me.portPrompt.Name = "portPrompt"
            Me.portPrompt.Size = New System.Drawing.Size(28, 13)
            Me.portPrompt.TabIndex = 0
            Me.portPrompt.Text = "Port:"
            '
            'port
            '
            Me.port.Location = New System.Drawing.Point(373, 20)
            Me.port.MaxLength = 6
            Me.port.Name = "port"
            Me.port.Size = New System.Drawing.Size(52, 20)
            Me.port.TabIndex = 22
            '
            'hqpProfilesPrompt
            '
            Me.hqpProfilesPrompt.AutoSize = True
            Me.hqpProfilesPrompt.Location = New System.Drawing.Point(35, 60)
            Me.hqpProfilesPrompt.Name = "hqpProfile"
            Me.hqpProfilesPrompt.Size = New System.Drawing.Size(110, 13)
            Me.hqpProfilesPrompt.TabIndex = 0
            Me.hqpProfilesPrompt.Text = "HQPlayer devices:"
            '
            'activeHqpProfiles
            '
            Me.activeHqpProfiles.FormattingEnabled = True
            Me.activeHqpProfiles.Location = New System.Drawing.Point(36, 80)
            Me.activeHqpProfiles.Name = "activeHqpProfiles"
            Me.activeHqpProfiles.Size = New System.Drawing.Size(210, 120)
            Me.activeHqpProfiles.TabIndex = 23
            '
            'profileNamePrompt
            '
            Me.profileNamePrompt.AutoSize = True
            Me.profileNamePrompt.Location = New System.Drawing.Point(267, 80)
            Me.profileNamePrompt.Name = "profileNamePrompt"
            Me.profileNamePrompt.Size = New System.Drawing.Size(36, 13)
            Me.profileNamePrompt.TabIndex = 0
            Me.profileNamePrompt.Text = "Name:"
            '
            'profileName
            '
            Me.profileName.Enabled = True
            Me.profileName.Location = New System.Drawing.Point(315, 80)
            Me.profileName.Name = "profileName"
            Me.profileName.Size = New System.Drawing.Size(205, 20)
            Me.profileName.TabIndex = 30
            '
            'hqp ipAddress label
            '
            Me.hqpIpPrompt.AutoSize = True
            Me.hqpIpPrompt.Location = New System.Drawing.Point(267, 110)
            Me.hqpIpPrompt.Name = "ipAddress"
            Me.hqpIpPrompt.Size = New System.Drawing.Size(186, 13)
            Me.hqpIpPrompt.TabIndex = 0
            Me.hqpIpPrompt.Text = "IP address:"
            '
            'hqpIp
            '
            Me.hqpIp.Enabled = True
            Me.hqpIp.Location = New System.Drawing.Point(315, 130)
            Me.hqpIp.Name = "hqpIp"
            Me.hqpIp.Size = New System.Drawing.Size(205, 20)
            Me.hqpIp.TabIndex = 31
            '
            'hqpVersion label
            '
            Me.hqpVersionPrompt.AutoSize = True
            Me.hqpVersionPrompt.Location = New System.Drawing.Point(267, 160)
            Me.hqpVersionPrompt.Name = "hqpVersionPrompt"
            Me.hqpVersionPrompt.Size = New System.Drawing.Size(186, 13)
            Me.hqpVersionPrompt.TabIndex = 0
            Me.hqpVersionPrompt.Text = "Version:"
            '
            'hqpVersion
            '
            Me.hqpVersion.Enabled = False
            Me.hqpVersion.Location = New System.Drawing.Point(315, 180)
            Me.hqpVersion.Name = "hqpVersion"
            Me.hqpVersion.Size = New System.Drawing.Size(205, 20)
            Me.hqpVersion.TabIndex = 31
            '
            'addProfileButton
            '
            Me.addProfileButton.Location = New System.Drawing.Point(36, 195)
            Me.addProfileButton.Name = "addProfileButton"
            Me.addProfileButton.Size = New System.Drawing.Size(60, 23)
            Me.addProfileButton.TabIndex = 23
            Me.addProfileButton.Text = "Add"
            Me.addProfileButton.UseVisualStyleBackColor = True
            '
            'removeProfileButton
            '
            Me.removeProfileButton.Location = New System.Drawing.Point(105, 195)
            Me.removeProfileButton.Name = "removeProfileButton"
            Me.removeProfileButton.Size = New System.Drawing.Size(60, 23)
            Me.removeProfileButton.TabIndex = 24
            Me.removeProfileButton.Text = "Remove"
            Me.removeProfileButton.UseVisualStyleBackColor = True
            '
            'testProfileButton
            '
            Me.testProfileButton.Location = New System.Drawing.Point(176, 195)
            Me.testProfileButton.Name = "testProfileButton"
            Me.testProfileButton.Size = New System.Drawing.Size(60, 23)
            Me.testProfileButton.TabIndex = 24
            Me.testProfileButton.Text = "Test"
            Me.testProfileButton.UseVisualStyleBackColor = True
            '
            '
            'logDebugInfo
            '
            Me.logDebugInfo.AutoSize = True
            Me.logDebugInfo.Location = New System.Drawing.Point(36, 252)
            Me.logDebugInfo.Name = "logDebugInfo"
            Me.logDebugInfo.Size = New System.Drawing.Size(127, 17)
            Me.logDebugInfo.TabIndex = 60
            Me.logDebugInfo.Text = "Log debug information"
            Me.logDebugInfo.UseVisualStyleBackColor = True
            '
            'viewButton
            '
            Me.viewButton.Location = New System.Drawing.Point(200, 250)
            Me.viewButton.Name = "closeButton"
            Me.viewButton.Size = New System.Drawing.Size(80, 23)
            Me.viewButton.TabIndex = 62
            Me.viewButton.Text = "View"
            Me.viewButton.UseVisualStyleBackColor = True
            '
            'closeButton
            '
            Me.closeButton.Location = New System.Drawing.Point(440, 250)
            Me.closeButton.Name = "closeButton"
            Me.closeButton.Size = New System.Drawing.Size(80, 23)
            Me.closeButton.TabIndex = 62
            Me.closeButton.Text = "Cancel"
            Me.closeButton.UseVisualStyleBackColor = True
            '
            'saveButton
            '
            Me.saveButton.Location = New System.Drawing.Point(351, 250)
            Me.saveButton.Name = "saveButton"
            Me.saveButton.Size = New System.Drawing.Size(80, 23)
            Me.saveButton.TabIndex = 61
            Me.saveButton.Text = "Save"
            Me.saveButton.UseVisualStyleBackColor = True
            '
            '
            'SettingsDialog
            '
            Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
            Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
            Me.ClientSize = New System.Drawing.Size(550, 290)

            Me.Controls.Add(helpLabel)
            Me.Controls.Add(Me.port)
            Me.Controls.Add(Me.ipAddress)
            Me.Controls.Add(Me.ipAddressPrompt)
            Me.Controls.Add(Me.viewButton)
            Me.Controls.Add(Me.portPrompt)
            Me.Controls.Add(Me.profileName)
            Me.Controls.Add(Me.hqpIp)
            Me.Controls.Add(Me.removeProfileButton)
            Me.Controls.Add(Me.testProfileButton)
            Me.Controls.Add(Me.addProfileButton)
            Me.Controls.Add(Me.activeHqpProfiles)
            Me.Controls.Add(Me.saveButton)
            Me.Controls.Add(Me.closeButton)
            Me.Controls.Add(Me.profileNamePrompt)
            Me.Controls.Add(Me.hqpIpPrompt)
            Me.Controls.Add(Me.hqpProfilesPrompt)
            Me.Controls.Add(Me.logDebugInfo)
            Me.Controls.Add(Me.hqpVersion)
            Me.Controls.Add(Me.hqpVersionPrompt)
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.Name = "SettingsDialogHQP"
            Me.ShowIcon = False
            Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
            Me.Text = "HQPlayer Plugin Settings"
            Me.ResumeLayout(False)
            Me.PerformLayout()

        End Sub

        Private logDebugInfo As System.Windows.Forms.CheckBox
        Private hqpVersionPrompt As System.Windows.Forms.Label
        Private hqpVersion As System.Windows.Forms.TextBox
        Private hqpProfilesPrompt As System.Windows.Forms.Label
        Private hqpIpPrompt As System.Windows.Forms.Label
        Private hqpIp As System.Windows.Forms.TextBox
        Private profileName As System.Windows.Forms.TextBox
        Private profileNamePrompt As System.Windows.Forms.Label
        Private portPrompt As System.Windows.Forms.Label
        Private port As System.Windows.Forms.TextBox
        Private closeButton As System.Windows.Forms.Button
        Private saveButton As System.Windows.Forms.Button
        Private activeHqpProfiles As System.Windows.Forms.ListBox
        Private addProfileButton As System.Windows.Forms.Button
        Private removeProfileButton As System.Windows.Forms.Button
        Private testProfileButton As System.Windows.Forms.Button
        Private viewButton As System.Windows.Forms.Button
        Private ipAddressPrompt As System.Windows.Forms.Label
        Private ipAddress As System.Windows.Forms.ComboBox
        Private helpLabel As System.Windows.Forms.LinkLabel

    End Class  ' SettingsDialog
End Class
