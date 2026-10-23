# Run with Libraries selected and no editor open, from ci_ui_smoke.ps1.
# Only rename a new empty directory on the disposable runner, never a resource source.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'CI picker regression required.' }
$pickerPointer = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'RHI-CS/bootstrap.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$fixturePrefix = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'RhiTests')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($pickerPointer.Root).StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Picker regression requires the disposable UI fixture.'
}
$pickerRoot = Join-Path ([IO.Path]::GetTempPath()) ('RhiTests\picker-' + [Guid]::NewGuid().ToString('N'))
$pickerMoved = $pickerRoot + '-moved'
[IO.Directory]::CreateDirectory($pickerRoot) | Out-Null

function Get-PickerDraft {
    @('VersionLabelInput', 'VersionSourceInput', 'VersionTargetOverride') | ForEach-Object {
        (Find-Control -id $_ -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    }
}
function Assert-PickerDraft([string[]]$expected) {
    $actual = @(Get-PickerDraft)
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ($actual[$i] -cne $expected[$i]) { throw "Picker changed unsaved field $i." }
    }
}
function Find-NativePicker {
    $filter = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, '#32770'))
    return [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $filter)
}
function Assert-PickerConfiguration {
    if ([IO.File]::ReadAllText($pickerWorkspace) -cne $pickerState) { throw 'Picker error or cancellation changed configuration.' }
}
function Assert-NoPickerError {
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $errorId = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'VersionPickerError')
    $feedback = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $errorId)
    if ($feedback -and $feedback.Current.Name) { throw 'Picker retained stale error guidance.' }
}

try {
    Invoke-LibraryControl 'LibraryEdit'
    $rootInput = (Find-Control -id 'LibrarySourceRootInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pickerOriginalRoot = $rootInput.Current.Value
    $rootInput.SetValue($pickerRoot)
    Invoke-Control 'Save'
    Find-Control -id 'LibraryAddVersion' -Enabled | Out-Null
    $pickerWorkspace = Join-Path $pickerPointer.Root 'workspace.json'
    $pickerState = [IO.File]::ReadAllText($pickerWorkspace)
    Invoke-LibraryControl 'LibraryAddVersion'
    (Find-Control -id 'VersionLabelInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Picker draft retained')
    (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($legacySource)
    (Find-Control -id 'VersionTargetOverride' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('picker-draft')
    $pickerDraft = @(Get-PickerDraft)

    foreach ($pickerId in @('VersionChooseFolder', 'VersionChooseFile')) {
        [IO.Directory]::Move($pickerRoot, $pickerMoved)
        (Find-Control -id $pickerId -Enabled).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $feedback = Find-Control -id 'VersionPickerError'
        if (-not $feedback.Current.Name.StartsWith((Label 'LibraryFolderMissing')) -or
            -not $feedback.Current.Name.Contains($pickerRoot)) { throw "Wrong missing-folder guidance: $pickerId" }
        Find-Control -id 'ValidatedForm' | Out-Null
        Find-Control -name (Label 'Cancel') -Action -Enabled | Out-Null
        Find-Control -name (Label 'Save') -Action -Enabled | Out-Null
        if (Find-NativePicker) { throw "Missing-folder check unexpectedly opened a native picker: $pickerId" }
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $errorTitle = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, (Label 'Error'))
        if ($window.FindFirst([Windows.Automation.TreeScope]::Descendants, $errorTitle)) { throw 'Picker opened a nested error dialog.' }
        Assert-PickerDraft $pickerDraft
        Assert-PickerConfiguration
        Capture ("picker-missing-" + $pickerId)

        if ($pickerId -eq 'VersionChooseFolder') {
            # A manually corrected source is not obscured by an unrelated stale picker error.
            $sourceValue = (Find-Control -id 'VersionSourceInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
            $sourceValue.SetValue($legacySource + '.manual')
            Start-Sleep -Milliseconds 200
            Assert-NoPickerError
            $sourceValue.SetValue($legacySource)
        }

        # Retry after the library becomes available; cancellation must retain all draft values.
        [IO.Directory]::Move($pickerMoved, $pickerRoot)
        (Find-Control -id $pickerId -Enabled).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $native = $null
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            Start-Sleep -Milliseconds 150
            $native = Find-NativePicker
            if ($native) { break }
        }
        if (-not $native) { throw "Picker cannot recover after folder returns: $pickerId" }
        $nodes = $native.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
        $names = @($nodes | ForEach-Object { $_.Current.Name }) -join "`n"
        if (-not $names.Contains($pickerRoot)) { throw "Recovered picker lost its initial folder: $pickerId" }
        $cancel = @($nodes | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -match '^(Cancel|\u53d6\u6d88)$' }) | Select-Object -First 1
        if (-not $cancel) { throw 'Native picker cancel missing.' }
        $cancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 250
        Assert-PickerDraft $pickerDraft
        Assert-PickerConfiguration
        Assert-NoPickerError
    }
    Invoke-Control 'Cancel'
    Assert-PickerConfiguration
    Invoke-LibraryControl 'LibraryEdit'
    (Find-Control -id 'LibrarySourceRootInput' -IncludeOffscreen).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($pickerOriginalRoot)
    Invoke-Control 'Save'
    Find-Control -id 'LibraryAddVersion' -Enabled | Out-Null
    [IO.File]::WriteAllText((Join-Path $captureRoot 'picker-errors-result.txt'),
        'PASS: folder and file pickers keep missing-library errors in the editor; drafts and configuration retained; retry and native cancellation recover without stale feedback.', [Text.Encoding]::UTF8)
} finally {
    if ([IO.Directory]::Exists($pickerMoved) -and -not [IO.Directory]::Exists($pickerRoot)) {
        [IO.Directory]::Move($pickerMoved, $pickerRoot)
    }
    # Non-recursive deletion intentionally refuses unexpected contents.
    if ([IO.Directory]::Exists($pickerRoot)) { [IO.Directory]::Delete($pickerRoot, $false) }
}
