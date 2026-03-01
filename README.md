*If required by a game moderator I will close this repository.*
# DISCLAIMER
- Do NOT use this overnight or unattended. You *WILL* get banned.
- This was a hobby project. I will not be actively developing this tool.
- I am new to F#. It took me 6.5 hours straight (down to 6 AM) to make this program, and it is not a final product.

# ⚠️Caution⚠️
- **Roblox does not permit the use of third-party software for automation.** You are not allowed to use macros unless you are actively present at your keyboard.
- This program uses `user32.dll` Windows API calls to simulate mouse movement, mouse clicks, and more. While it only interacts with preset screen locations, be aware of the security implications of running automation software.
- Game moderators in Arcane Odyssey will **absolutely** ban you, likely permanently, if you are caught using macros without being present. The same goes for all Roblox games.
- **I am the distributor of this project and responsible for making it available. I am NOT responsible for how users choose to use it. If you get banned, it is entirely on you.**

## This repository is not affiliated with Roblox Corporation, Arcane Odyssey or Vetex Games.

---

<img width="567" height="379" alt="Screenshot_55" src="https://github.com/user-attachments/assets/904f4ed8-68c3-45b6-8e1b-82d48388e55b" />

# What is this?
A standalone, robust, lightweight auto-fishing tool for Arcane Odyssey (Roblox) written in F#. It uses image recognition (OpenCV) to detect fishing prompts and automatically clicks to reel in fish. Fast but potentially inaccurate.
This is a solo project, it is not open for contribution or pull requests.

## Features
- Simple configuration -- set clicking positions
- Dynamic Reeling -- keeps reeling until a catch
- Saves progress and settings (`.../config.json`)
- Panic button -- CTRL+P to turn it OFF.
- Light-weight image recognition
###
- **TODO** - "Next Lure Rotation" status
- **TODO** - "Time Enabled" status
###
- **LATER** - A User-friendly GUI -- Organized, Tooltips, etc.
- **MAYBE NOT** - Automate Fleet repairs -- requires kernel, I don't wanna do that...

## Developer Notes
- The large file is due to embedded libraries in the build.
- The program has embedded images of the fishing prompt and various in-game notifications
- The image recognition API is based on OpenCvSharp4, it only captures Roblox (when focused), a small aspect ratio of its window, to detect fishing prompts.

## Requirements
*These do not apply to standalone builds.*
- Windows (uses Win32 APIs)
- .NET 8.0 Runtime
- OpenCvSharp4 (will change in the future)

## Usage
1. [Download](https://github.com/FlyinC4T/AO-Fishing-Tool/releases/latest)
2. Position Yourself
3. Set fishing rod
4. Press "Toggle"
5. Watch a movie

---

**Use at your own risk.**
