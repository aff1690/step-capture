# StepCapture

A Windows desktop tool that records what you do and turns it into a step-by-step guide.
Click **Record**, do the task, click **Stop**. Every click becomes a step with a screenshot that shows the
cursor and a highlight ring where you clicked. Typing becomes a "Type ..." step and shortcuts become
"Press Ctrl + S" steps. Edit the steps, then export to HTML, Word, PDF or Markdown.

## Features

- **Recording** with a small floating bar (Pause / Stop) that stays out of its own screenshots.
  Hotkeys: **Ctrl+Shift+F9** pauses or resumes, **Ctrl+Shift+F10** stops.
- **Screenshots on every click** of the monitor you clicked on, with the cursor drawn in and the click point
  highlighted. Each monitor is captured at its real resolution, including mixed-scaling setups.
- **Automatic captions** from Windows UI Automation, such as *Click the "Save" button* or
  *Type "quarterly report" in the "File name" field*. Double-clicks and right-clicks are detected.
- **Keystrokes**: consecutive typing is merged into one step (Backspace is honored). Enter, Tab, Esc, Delete,
  F-keys and Ctrl/Alt/Win shortcuts become their own steps. Arrow keys and lone modifiers are ignored.
- **Password safety**: typing into password fields is recorded only as "Type your password". The text is never stored.
- **Editor**: rename the guide, edit captions, add notes, reorder (Ctrl+Up / Ctrl+Down), delete (Del),
  insert text-only steps, and blur or crop part of a screenshot.
- **Screenshot markup**: draw arrows, lines, boxes, circles, freehand pen, highlighter, text and numbered
  callouts (1, 2, 3…) in eight colors and three sizes. Hold Shift for straight angles and perfect squares/circles.
  Shapes stay editable: with the Select tool (V) click a shape to select it, drag to move, drag a handle to resize,
  pick a color or size to change it, double-click text to edit it, press Delete to remove it, or right-click for
  more. Arrow keys nudge the selected shape. Ctrl+Z undoes any change.
  Shortcuts: V select, B blur/crop area, A arrow, L line, R box, E circle, P pen, H highlight, T text, N number.
- **Zoom** while editing screenshots: the − / + buttons, Ctrl+scroll, Ctrl+plus/minus, Fit (Ctrl+0) and 100% (Ctrl+1).
- **Letterhead**: in the Export menu, choose a Word template (.dotx or .docx) with your company letterhead once,
  then switch between "Letterhead" and "Blank page". Word exports are built on the template itself (header,
  footer, margins and styles); PDF and HTML exports use its header picture and footer text.
- **Light and dark appearance**: toggle with the pill in the top-right corner. The choice is remembered; the first run follows your Windows app theme.
- **Save / open** guides as `.stepguide` files (a zip of JSON and PNGs).
- **Export** to a self-contained HTML page, Word (.docx), PDF (US Letter) or Markdown with an images folder.

## Install

Every push to GitHub builds the app on a Windows machine with GitHub Actions.

1. Open the repository on GitHub and go to **Actions → Build**, then click the most recent successful run.
   (When a version tag such as `v0.2.0` is pushed, the same files are attached to a GitHub **Release**.)
2. Under **Artifacts**, download **StepCapture-Setup**, unzip it and run `StepCapture-Setup-<version>.exe`.
3. The installer needs no admin rights by default. It installs for your account, adds a Start menu entry,
   optionally a desktop shortcut, and makes `.stepguide` files open in StepCapture. It also offers an
   "install for all users" option, which asks for admin rights.
4. Uninstall from **Settings → Apps** (or the Start menu's "Uninstall StepCapture"). Your settings in
   `%LOCALAPPDATA%\StepCapture` are kept, so reinstalling remembers your theme and letterhead.

Running a newer installer upgrades the existing install in place.

Because the installer isn't code-signed yet, Windows SmartScreen may say "Windows protected your PC".
Click **More info → Run anyway**.

### Portable exe

If you'd rather not install anything, the same build also produces:

- **StepCapture-win-x64**: a single `StepCapture.exe` (about 70 MB) that runs on any 64-bit Windows 10/11 PC.
- **StepCapture-win-x64-needs-dotnet8**: a much smaller `StepCapture.exe` that needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build it yourself on Windows

Requirements: Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(or Visual Studio 2022 with the ".NET desktop development" workload).

```powershell
git clone https://github.com/aff1690/step-capture.git
cd step-capture

# run the unit tests
dotnet test

# run the app from source
dotnet run --project src/StepCapture.App

# produce a standalone single-file exe in .\publish
dotnet publish src/StepCapture.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
```

In Visual Studio, open `StepCapture.sln`, set **StepCapture.App** as the startup project and press F5.

## Quick test checklist

1. Start StepCapture and click **Record**. The main window minimizes and the recording bar appears at the top of the screen.
2. Open Notepad from the Start menu, type a sentence, press **Ctrl+S**, type a file name, and press **Enter**.
3. Click **Stop** on the bar (or press Ctrl+Shift+F10). The editor shows one step per action.
4. Select a step, drag across part of the screenshot and click **Blur selection**.
5. Use **Export → PDF** and **Export → Word document** and open the results.

If anything goes wrong, details are written to `%LOCALAPPDATA%\StepCapture\error.log`.

## Project layout

| Path | What it is |
| --- | --- |
| `src/StepCapture.Core` | Cross-platform logic: the step model, turning raw clicks and keys into steps, captions, exporters and the `.stepguide` file format. |
| `src/StepCapture.App` | The WPF Windows app: global input hooks, screen capture, UI Automation lookups, the recording bar and the editor. |
| `tests/StepCapture.Core.Tests` | xUnit tests for the core logic and exporters. |
| `installer/StepCapture.iss` | Inno Setup script for the installer. |
| `.github/workflows/build.yml` | Builds, tests and publishes the installer and portable exes on `windows-latest`. |

How recording works: global low-level mouse and keyboard hooks run on their own thread. On each click, the
screen is captured right away, before the clicked app reacts. A worker thread then asks UI Automation what
was clicked, draws the highlight, and feeds the step builder, which merges typing and detects double-clicks.

## Known limitations

- Windows doesn't let a normal app see input aimed at apps running **as administrator**. To record those, run StepCapture as administrator too.
- Screenshots cover the whole monitor. Use **Crop to selection** to focus on part of it.
- Drags and mouse-wheel scrolling are not recorded as steps yet.
- Recorded keystrokes appear in captions as typed (except in password fields), so review guides before sharing them.

## Libraries

- [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) (MIT) for Word export
- [PDFsharp](https://www.pdfsharp.com/) (MIT) for PDF export
