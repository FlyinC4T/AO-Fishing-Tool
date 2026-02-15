# DISCLAIMER
- Do NOT use this overnight or unattended. You *WILL* get banned.
- This was a hobby project. I will not be actively developing this tool.
- I am new to F#. It took me 6.5 hours straight (down to 6 AM) to make this program, and it is not a final product.

# ⚠️Caution⚠️
- **Roblox does not permit the use of third-party software for automation.** You are not allowed to use macros unless you are actively present at your keyboard.
- This program uses `user32.dll` Windows API calls to simulate mouse movement and mouse click inputs. While it only interacts with preset screen locations, be aware of the security implications of running automation software.
- Game moderators in Arcane Odyssey will **absolutely** ban you, likely permanently, if you are caught using macros without being present. The same goes for all Roblox games.
- **I am the distributor of this project and responsible for making it available. I am NOT responsible for how users choose to use it. If you get banned, it is entirely on you.**

## This project is not affiliated with Roblox Corporation, Arcane Odyssey, Vetex, or Vetex Games.

---

# What is this?
An insanely lightweight auto-fishing tool for Arcane Odyssey (Roblox) written in F#. It uses image recognition (OpenCV) to detect fishing prompts and automatically clicks to reel in fish, efficiently.
This is a solo project, it is not open for contribution or pull requests.

## Features
- Template-based image recognition -- you choose the screenshot
- Configurable search area (resize/move window)
- DEBUGGING: Mouse coordinate display tool
<img width="403" height="299" alt="image" src="https://github.com/user-attachments/assets/15498c02-92e5-4a46-bc2b-1f8aa1594429" />

## TODO (In order)
- Configurable click patterns -- *currently they are preset and stuck inside the code untouchable unless you run the code yourself, given the coordinate pointer tool.*
  - This would include bait, lure, rod and offhand.
- Include .NET 8.0 Runtime in builds
- Include OpenCvSharp4 in builds
- Config file (for saving)
- A more user-friendly GUI (man it suuuucks :sob:)

## Requirements
- Windows (uses Win32 APIs)
- .NET 8.0 Runtime
- OpenCvSharp4 (will change in the future)

## For Developers (Manually installing)
- Currently the numbers at the bottom are the clicking locations.

## Usage
1. Load a template or screenshot image of the fishing prompt (the "!" icon)
2. Position/resize the window over where the prompt appears in-game
3. Click "Toggle" to start
4. Lay back, watch a movie, or scroll some reels.

---

**Use at your own risk.**
