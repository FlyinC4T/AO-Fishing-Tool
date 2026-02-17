open System
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open OpenCvSharp
open System.Drawing

// Check if a specific window handle is focused
[<DllImport("user32.dll")>]
extern IntPtr GetForegroundWindow()

[<DllImport("user32.dll")>]
extern bool GetWindowRect(IntPtr hWnd, Drawing.Rectangle& lpRect)

[<DllImport("user32.dll", CharSet = CharSet.Auto)>]
extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount)

[<DllImport("user32.dll")>]
extern void keybd_event(byte bVk, byte bScan, uint32 dwFlags, UIntPtr dwExtraInfo)

[<DllImport("user32.dll")>]
extern void mouse_event(uint32 dwFlags, uint32 dx, uint32 dy, uint32 dwData, int dwExtraInfo)

[<DllImport("user32.dll")>]
extern bool RegisterHotKey(IntPtr hWnd, int id, uint32 fsModifiers, uint32 vk)

[<DllImport("user32.dll")>]
extern bool UnregisterHotKey(IntPtr hWnd, int id)

let getWindowTitle (hwnd: IntPtr) =
    let sb = new System.Text.StringBuilder(256)
    GetWindowText(hwnd, sb, sb.Capacity) |> ignore
    sb.ToString()

let pressKey (key: Keys) =
    keybd_event(byte key, 0uy, 0u, UIntPtr.Zero)   // Key down
    Thread.Sleep(50)
    keybd_event(byte key, 0uy, 2u, UIntPtr.Zero)   // Key up

let clickAt (x: int, y: int) =
    let screenWidth = Screen.PrimaryScreen.Bounds.Width
    let screenHeight = Screen.PrimaryScreen.Bounds.Height
    
    // Convert to absolute coordinates (0-65535 range)
    let absX = uint32((x * 65536) / screenWidth)
    let absY = uint32((y * 65536) / screenHeight)
    
    // Move cursor
    mouse_event(0x8001u, absX, absY, 0u, 0)  // MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE
    Thread.Sleep(50)
    // Click down
    mouse_event(0x8002u, absX, absY, 0u, 0)  // MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN
    Thread.Sleep(Random().Next(100, 200))
    // Click up
    mouse_event(0x8004u, absX, absY, 0u, 0)  // MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTUP

let clickAtScreenRatio (ratioX: float, ratioY: float) =
    let bounds = Screen.PrimaryScreen.Bounds
    let x = int(float bounds.Width * ratioX)
    let y = int(float bounds.Height * ratioY)
    clickAt(x, y)

let loadEmbeddedTemplate () =
    let assembly = System.Reflection.Assembly.GetExecutingAssembly()
    // Name format: ProjectName.filename.png
    let resourceName = assembly.GetManifestResourceNames() |> Array.tryFind (fun n -> n.EndsWith("FishPrompt.png"))
    
    match resourceName with
    | Some name ->
        use stream = assembly.GetManifestResourceStream(name)
        use bmp = new Bitmap(stream)
        let tmpPath = System.IO.Path.GetTempFileName() + ".png"
        bmp.Save(tmpPath)
        Some(new Mat(tmpPath))
    | None ->
        printfn "Embedded template not found!"
        None

let captureArea (bounds: Rectangle) =
    let bmp = new Bitmap(bounds.Width, bounds.Height)
    use g = Graphics.FromImage(bmp)
    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size)
    bmp

let findTemplate (screen: Mat) (template: Mat) (offsetX: int) (offsetY: int) =
    try
        // Convert both to grayscale for consistent matching
        use screenGray = new Mat()
        use templateGray = new Mat()
        
        Cv2.CvtColor(screen, screenGray, ColorConversionCodes.BGR2GRAY)
        Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY)
        
        use result = new Mat()
        Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.CCoeffNormed)
        
        let mutable minVal = 0.0
        let mutable maxVal = 0.0
        let mutable minLoc = OpenCvSharp.Point()
        let mutable maxLoc = OpenCvSharp.Point()
        Cv2.MinMaxLoc(result, &minVal, &maxVal, &minLoc, &maxLoc)
        
        printfn $"Match confidence: {maxVal:F2}"
        
        if maxVal > 0.7 then
            Some(offsetX + maxLoc.X + templateGray.Width / 2, offsetY + maxLoc.Y + templateGray.Height / 2)
        else
            None
    with
    | ex -> 
        printfn $"Error: {ex.Message}"
        None

// Convert Bitmap to OpenCV Mat
let bitmapToMat (bmp: Bitmap) =
    OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp)

type HotbarSlot =
    | Slot0 = 0 // Slot 10
    | Slot1 = 1
    | Slot2 = 2
    | Slot3 = 3
    | Slot4 = 4
    | Slot5 = 5
    | Slot6 = 6
    | Slot7 = 7
    | Slot8 = 8
    | Slot9 = 9

type MainForm() as this =
    inherit Form()

    let mutable slotFishingRod = HotbarSlot.Slot9
    let mutable lureFishingRod = HotbarSlot.Slot0

    let mutable isRunning = false
    let mutable useBait = false
    let mutable baitPointerActive = false
    let mutable baitPosition : Point = Point.Empty
    let mutable fishCaught : int32 = 0
    let mutable statusCaught : Control = null
    let mutable statusBaitSet : Control = null
    let mutable btnToggle : Control = new Button(Text = "Toggle", Location = Point(20, 10), Size = Size(100, 20), ForeColor = Color.White, BackColor = Color.Red)
    let mutable btnUseBait : Control = new Button(Text = "Use Bait", Location = Point(20, 30), Size = Size(80, 20), ForeColor = Color.White, BackColor = Color.Red)
    let mutable btnBaitPos : Control = new Button(Text = "X", Location = Point(100, 30), Size = Size(20, 20), ForeColor = Color.White, BackColor = Color.Blue)
    let mutable btnFRodSlot : Button = new Button(Text = "9", Location = Point(20, 50), Size = Size(20, 20), BackColor = Color.White)
    let mutable btnLureSlot : Button = new Button(Text = "0", Location = Point(20, 70), Size = Size(20, 20), BackColor = Color.White)
    let mutable mouseLabel : Form = null
    let mutable mouseTracker : Timer = null  // Add this field
    let mutable lastFixRotation = DateTime.Now
    let mutable cancelSource = new System.Threading.CancellationTokenSource()

    let template: Mat option = loadEmbeddedTemplate()
    let timer = new Timer(Interval = 200)
    
    do
        this.FormBorderStyle <- FormBorderStyle.SizableToolWindow
        this.StartPosition <- FormStartPosition.CenterScreen
        this.TopMost <- true
        this.Text <- "AO Fishing Tool"
        this.Size <- Size(210, 150)
        this.MinimumSize <- Size(210, 150)
        this.MaximumSize <- Size(400, 600)
        this.BackColor <- Drawing.Color.Gray
        this.Opacity <- 1.0

        this.Load.Add(fun _ ->
            let CTRL = 0x0002u
            let P_KEY = 0x50u
            RegisterHotKey(this.Handle, 1, CTRL, P_KEY) |> ignore
        )
        this.FormClosed.Add(fun _ -> 
            UnregisterHotKey(this.Handle, 1) |> ignore
            // possibly config saving in here...
        )
        
        btnToggle.Click.Add(fun _ -> this.ToggleFishing())
        btnUseBait.Click.Add(fun _ -> this.ToggleBait())
        btnBaitPos.Click.Add(fun _ -> this.ToggleBaitPointer())

        statusCaught <- new Label(Text = "Caught: 0", Location = Point(120, 12), Size = Size(100, 15), ForeColor = Color.White)
        statusBaitSet <- new Label(Text = "Not set", Location = Point(120, 32), Size = Size(100, 15), ForeColor = Color.White)

        btnFRodSlot.Click.Add(fun _ -> this.CycleSlot((fun () -> slotFishingRod), (fun v -> slotFishingRod <- v), btnFRodSlot))
        btnLureSlot.Click.Add(fun _ -> this.CycleSlot((fun () -> lureFishingRod), (fun v -> lureFishingRod <- v), btnLureSlot))
        
        let lblFishingRod = new Label(Text = "- Rod Slot", Location = Point(40, 50), Size = Size(100, 20), ForeColor = Color.White)
        let lblLure = new Label(Text = "- Lure Slot", Location = Point(40, 70), Size = Size(100, 20), ForeColor = Color.White)

        this.Controls.AddRange([| btnToggle; btnUseBait; btnBaitPos; statusCaught; statusBaitSet; btnFRodSlot; btnLureSlot; lblFishingRod; lblLure; |])
        
        this.InitializePointer()

        timer.Tick.Add(fun _ -> this.CheckFishIcon())

    member this.InitializePointer() =
        mouseLabel <- new Form(
            FormBorderStyle = FormBorderStyle.None,
            BackColor = Drawing.Color.Black,
            ForeColor = Drawing.Color.Lime,
            TopMost = true,
            ShowInTaskbar = false,
            Size = Size(150, 35),
            StartPosition = FormStartPosition.Manual
        )
        
        let lblMouseCoords = new Label(
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 9.0f, FontStyle.Bold)
        )
        mouseLabel.Controls.Add(lblMouseCoords)
        
        mouseTracker <- new Timer(Interval = 16)
        mouseTracker.Tick.Add(fun _ ->
            let pos = Cursor.Position
            let screenBounds = Screen.PrimaryScreen.Bounds
            lblMouseCoords.Text <- $"Pixel: ({pos.X}, {pos.Y}))"
            mouseLabel.Location <- Point(pos.X - (mouseLabel.Size.Width/2), pos.Y - 50)

            // listen for click event to set bait position
            if Control.MouseButtons = MouseButtons.Left then
                Thread.Sleep(100) // debounce

                // respect user focus - only set bait if our tool is not focused to avoid misclicks
                let foregroundWindow = GetForegroundWindow()
                if not (foregroundWindow = this.Handle) then
                    this.ToggleBaitPointer() // turn off pointer after setting position
                    baitPosition <- pos
                    statusBaitSet.Text <- $"{pos.X}, {pos.Y}"
                    printfn $"Bait position set at ({pos.X}, {pos.Y})"
        )

    member this.ToggleBaitPointer() =
        baitPointerActive <- not baitPointerActive
        if baitPointerActive then
            mouseTracker.Start()
            mouseLabel.Show()
        else
            mouseTracker.Stop()
            mouseLabel.Hide()

    member this.ToggleFishing() =
        isRunning <- not isRunning
        btnToggle.BackColor <- (if isRunning then Color.Green else Color.Red) // no ternary is unswag

        if isRunning then
            timer.Start()
            printfn "Fishing started at %A" DateTime.Now
        if not isRunning then
            timer.Stop()
            printfn "Fishing stopped at %A" DateTime.Now

    member this.ToggleBait() =
        if baitPosition = Point.Empty then
            MessageBox.Show("Set bait click position first.") |> ignore
        else
            useBait <- not useBait
            btnUseBait.BackColor <- (if useBait then Color.Green else Color.Red) // no ternary is unswag

    member this.CycleSlot(getSlot: unit -> HotbarSlot, setSlot: HotbarSlot -> unit, btn: Button) =
        let next =
            match getSlot() with
            | HotbarSlot.Slot9 -> HotbarSlot.Slot0
            | s -> enum<HotbarSlot>(int s + 1)
        setSlot(next)
        btn.Text <- (int next).ToString()
        
    member this.GetSearchBounds() =
        let hwnd = GetForegroundWindow()
        let mutable rect = Drawing.Rectangle()
        GetWindowRect(hwnd, &rect) |> ignore
    
        // Search area as ratio of the focused window
        let x = int(float rect.X + float rect.Width * 0.35)
        let y = int(float rect.Y + float rect.Height * 0.1)
        let w = int(float rect.Width * 0.3)
        let h = int(float rect.Height * 0.4)
    
        Rectangle(x, y, w, h)

    member this.CheckFishIcon() =
        let foregroundWindow = GetForegroundWindow()
        let windowTitle = getWindowTitle(foregroundWindow)

        if isRunning && foregroundWindow <> this.Handle && windowTitle.Contains("Roblox") then
            if (DateTime.Now - lastFixRotation).TotalSeconds > 120.0 then
                this.FixFishingRod(true)
                printfn "Dropping lure..."
            else
                match template with
                | Some tmpl ->
                    let searchBounds = this.GetSearchBounds()
                    use screenBmp = captureArea(searchBounds)
                    use screenMat = bitmapToMat screenBmp
        
                    match findTemplate screenMat tmpl searchBounds.X searchBounds.Y with
                    | Some(x, y) ->
                        printfn $"Found fish icon at ({x}, {y})! Starting fishing loop..."
                        timer.Stop()  // Pause main timer
                        cancelSource <- new CancellationTokenSource()  // Fresh token
                        Async.Start(async { do! this.FishingLoop() }, cancelSource.Token)
                    | None ->
                        ()
                | None ->
                    this.ToggleFishing()
                    MessageBox.Show("load image first lol") |> ignore

    member this.FishingLoop() =
        async {
        let mutable fishingActive = true
        let mutable iconDisappeared = false
        let mutable disappearTime = DateTime.Now
        
        while fishingActive && not cancelSource.IsCancellationRequested do
            let searchBounds = this.Bounds
            use screenBmp = captureArea(searchBounds)
            use screenMat = bitmapToMat screenBmp
            
            match template with
            | Some tmpl ->
                match findTemplate screenMat tmpl searchBounds.X searchBounds.Y with
                | Some(x, y) ->
                    // Icon still visible, keep clicking
                    clickAtScreenRatio(0.75, 0.75)
                    do! Async.Sleep(Random().Next(150,200))
                | None ->
                    // Icon disappeared
                    if not iconDisappeared then
                        printfn "Icon disappeared, continuing to reel for 3.5 more seconds..."
                        iconDisappeared <- true
                        disappearTime <- DateTime.Now
                    
                    // Keep clicking for 2 seconds after icon disappears
                    let timeSinceDisappear = (DateTime.Now - disappearTime).TotalSeconds
                    if timeSinceDisappear < 10 then
                        clickAtScreenRatio(0.75, 0.75)
                        do! Async.Sleep(Random().Next(50,100))
                    else
                        printfn "Finished reeling - fish caught."
                        fishingActive <- false
                        this.Invoke(new Action(fun () -> this.ExpectedFishCaught())) |> ignore
            | None ->
                fishingActive <- false
            
            do! Async.Sleep(100)
    }

    member this.ExpectedFishCaught() =
        fishCaught <- fishCaught + 1
        statusCaught.Text <- $"Caught: {fishCaught}"
        this.FixFishingRod(false)
        
        if isRunning then timer.Start()  // Resume scanning

    member this.FixFishingRod(withLure: bool) =
        if withLure then
            lastFixRotation <- DateTime.Now
            pressKey(Keys.D0) //&clickAtScreenRatio(0.56, 0.93) // lure
            clickAtScreenRatio(0.75, 0.75)
            Thread.Sleep(Random().Next(1900,2000))
        else
            Thread.Sleep(500) // wai1t just incase it was thrown in the loop
            pressKey(Keys.D0) //clickAtScreenRatio(0.56, 0.93) // lure -- unselecting to fix rod
        
        pressKey(Keys.D9) //clickAtScreenRatio(0.6, 0.91) // rod
        clickAtScreenRatio(0.5, 0.75) // giant bait
        clickAtScreenRatio(0.75, 0.75) // general click

    // Panic Button (CTRL+P) to stop fishing immediately
    override this.WndProc(m: byref<Message>) =
        if m.Msg = 0x0312 then // 
            if isRunning then
                cancelSource.Cancel()  // Kill fishing loop
                this.ToggleFishing()
                printfn "Panic key pressed - fishing stopped"
        base.WndProc(&m)
        

[<STAThread>]
[<EntryPoint>]
let main argv =
    Application.EnableVisualStyles()
    Application.Run(new MainForm())
    0