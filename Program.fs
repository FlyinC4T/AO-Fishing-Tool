open System
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open OpenCvSharp
open System.Drawing
open System.IO
open System.Text.Json

// Check if a specific window handle is focused
[<DllImport("user32.dll")>]
extern IntPtr GetForegroundWindow()

[<DllImport("user32.dll")>]
extern bool GetWindowRect(IntPtr hWnd, Drawing.Rectangle& lpRect)

[<DllImport("user32.dll", CharSet = CharSet.Auto)>]
extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount)

[<DllImport("user32.dll")>]
extern void mouse_event(uint32 dwFlags, uint32 dx, uint32 dy, uint32 dwData, int dwExtraInfo)

[<DllImport("user32.dll")>]
extern bool RegisterHotKey(IntPtr hWnd, int id, uint32 fsModifiers, uint32 vk)

[<DllImport("user32.dll")>]
extern bool UnregisterHotKey(IntPtr hWnd, int id)

type Config = {
    UseBait: bool
    useLure: bool
    RodPosX: int
    RodPosY: int
    LurePosX: int
    LurePosY: int
    BaitPosX: int
    BaitPosY: int
    caughtFish: int
    caughtTreasure: int
    caughtSunken: int
}

let defaultConfig = {
    UseBait = false
    useLure = false
    RodPosX = 0
    RodPosY = 0
    LurePosX = 0
    LurePosY = 0
    BaitPosX = 0
    BaitPosY = 0
    caughtFish = 0
    caughtTreasure = 0
    caughtSunken = 0
}

let loadConfig() =
    let path = "config.json"
    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            JsonSerializer.Deserialize<Config>(json)
        with _ ->
            printfn "Failed to load config, using defaults"
            defaultConfig
    else
        defaultConfig

let saveConfig (config: Config) =
    let json = JsonSerializer.Serialize(config, JsonSerializerOptions(WriteIndented = true))
    File.WriteAllText("config.json", json)

let getWindowTitle (hwnd: IntPtr) =
    let sb = new System.Text.StringBuilder(256)
    GetWindowText(hwnd, sb, sb.Capacity) |> ignore
    sb.ToString()

let clickAt (x: int, y: int) =
    if Point.Empty.Equals(Point(x, y)) then
        printfn ("Empty click position attempted. Avoiding")
    else
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

let loadEmbeddedTemplate (filename: string) =
    let assembly = System.Reflection.Assembly.GetExecutingAssembly()
    let resourceName = assembly.GetManifestResourceNames() |> Array.tryFind (fun n -> n.EndsWith(filename))
    
    match resourceName with
    | Some name ->
        use stream = assembly.GetManifestResourceStream(name)
        use bmp = new Bitmap(stream)
        let tmpPath = System.IO.Path.GetTempFileName() + ".png"
        bmp.Save(tmpPath)
        Some(new Mat(tmpPath))
    | None ->
        printfn $"Embedded template '{filename}' not found!"
        None

let captureArea (bounds: Rectangle) =
    let bmp = new Bitmap(bounds.Width, bounds.Height)
    use g = Graphics.FromImage(bmp)
    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size)
    bmp

let findTemplate (screen: Mat) (template: Mat) (offsetX: int) (offsetY: int) (label: string) =
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
        
        printfn $"[{label}] Match confidence: {maxVal:F2}"
        
        (maxVal, 
         if maxVal > 0.7 then
            Some(offsetX + maxLoc.X + templateGray.Width / 2, offsetY + maxLoc.Y + templateGray.Height / 2)
         else
            None)
    with
    | ex -> 
        printfn $"[{label}] Error: {ex.Message}"
        (0.0, None)


// Convert Bitmap to OpenCV Mat
let bitmapToMat (bmp: Bitmap) =
    OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp)

type MainForm() as this =
    inherit Form()

    //let mutable slotRod = HotbarSlot.Slot9
    //let mutable slotLure = HotbarSlot.Slot0
    
    // Config and Data
    let mutable config = loadConfig()
    let mutable rodPosition : Point = Point.Empty
    let mutable baitPosition : Point = Point.Empty
    let mutable lurePosition : Point = Point.Empty
    let mutable totalCaught : int32 = 0
    let mutable statusCaught = [| 0; 0; 0 |]  // [fish, treasure, sunken]
    let mutable statusCaughtSession = [| 0; 0; 0 |]  // [fish, treasure, sunken]
    let mutable useBait = false
    let mutable useLure = false

    let mutable isRunning = false
    let mutable baitPointerActive = false
    let mutable rodPointerActive = false
    let mutable lurePointerActive = false
    let mutable statusTotalCaught : Control = null
    let mutable statusBaitSet : Control = null
    let mutable statusRodSet : Control = null
    let mutable statusLureSet : Control = null
    let mutable btnToggle  : Control = new Button(Text = "Toggle", Location = Point(20, 10), Size = Size(100, 20), ForeColor = Color.White, BackColor = Color.Red)
    let mutable btnUseLure : Control = new Button(Text = "Use Lure", Location = Point(20, 50), Size = Size(80, 20), ForeColor = Color.White, BackColor = Color.Red)
    let mutable btnUseBait : Control = new Button(Text = "Use Bait", Location = Point(20, 70), Size = Size(80, 20), ForeColor = Color.White, BackColor = Color.Red)
    let mutable btnRodPos  : Control = new Button(Text = "X", Location = Point(100, 30), Size = Size(20, 20), ForeColor = Color.White, BackColor = Color.Blue)
    let mutable btnLurePos : Control = new Button(Text = "X", Location = Point(100, 50), Size = Size(20, 20), ForeColor = Color.White, BackColor = Color.Blue)
    let mutable btnBaitPos : Control = new Button(Text = "X", Location = Point(100, 70), Size = Size(20, 20), ForeColor = Color.White, BackColor = Color.Blue)
    let mutable statCaughtFish : Control = null
    let mutable statCaughtTreasure : Control = null
    let mutable statCaughtSunken : Control = null
    let mutable mouseLabel : Form = null
    let mutable mouseTracker : Timer = null  // Add this field
    let mutable lastFixRotation = DateTime.Now
    let mutable cancelSource = new System.Threading.CancellationTokenSource()

    let timer = new Timer(Interval = 200)
    let windowSize = new Size(230, 220)
    let template: Mat option = loadEmbeddedTemplate("FishPrompt.png")
    let notificationTemplateFish: Mat option = loadEmbeddedTemplate("NotificationFish.png")
    let notificationTemplateTreasure: Mat option = loadEmbeddedTemplate("NotificationTreasure.png")
    let notificationTemplateSunken: Mat option = loadEmbeddedTemplate("NotificationSunken.png")

    do
        this.FormBorderStyle <- FormBorderStyle.SizableToolWindow
        this.StartPosition <- FormStartPosition.CenterScreen
        this.TopMost <- true
        this.Text <- "AO Fishing Tool"
        this.Size <- windowSize
        this.MinimumSize <- windowSize
        this.MaximumSize <- windowSize
        this.BackColor <- Drawing.Color.Gray
        this.Opacity <- 1.0

        this.Load.Add(fun _ ->
            let CTRL = 0x0002u
            let P_KEY = 0x50u
            RegisterHotKey(this.Handle, 1, CTRL, P_KEY) |> ignore
        )
        this.FormClosed.Add(fun _ -> 
            UnregisterHotKey(this.Handle, 1) |> ignore

            // Save config
            let newConfig = {
                UseBait = useBait
                useLure = useLure
                RodPosX = rodPosition.X
                RodPosY = rodPosition.Y
                LurePosX = lurePosition.X
                LurePosY = lurePosition.Y
                BaitPosX = baitPosition.X
                BaitPosY = baitPosition.Y
                caughtFish = statusCaught.[0]
                caughtTreasure = statusCaught.[1]
                caughtSunken = statusCaught.[2]
            }
            saveConfig newConfig
        )

        btnToggle.Click.Add(fun _ -> this.ToggleFishing())
        btnUseLure.Click.Add(fun _ -> this.ToggleLure())
        btnUseBait.Click.Add(fun _ -> this.ToggleBait())
        btnBaitPos.Click.Add(fun _ -> this.ToggleBaitPointer())
        btnRodPos.Click.Add(fun _ -> this.ToggleRodPointer())
        btnLurePos.Click.Add(fun _ -> this.ToggleLurePointer())

        statusTotalCaught  <- new Label(Text = "Caught: 0", Location = Point(120, 12), Size = Size(100, 15), ForeColor = Color.White)
        statusRodSet  <- new Label(Text = "Not set", Location = Point(120, 32), Size = Size(100, 15), ForeColor = Color.White)
        statusLureSet <- new Label(Text = "Not set", Location = Point(120, 52), Size = Size(100, 15), ForeColor = Color.White)
        statusBaitSet <- new Label(Text = "Not set", Location = Point(120, 72), Size = Size(100, 15), ForeColor = Color.White)
        
        let lblRodPos = new Label(Text = "Fishing Rod:", Location = Point(20, 32), Size = Size(100, 15), ForeColor = Color.White)
        let lblPanicKey = new Label(Text = "Panic Key: CTRL+P", Location = Point(20, 95), Size = Size(110, 15), ForeColor = Color.White)
        
        statCaughtFish <- new Label(Text = "Fish: 0 (Total: 0)", Location = Point(20, 120), Size = Size(150, 15), ForeColor = Color.White)
        statCaughtTreasure <- new Label(Text = "Treasure: 0 (Total: 0)", Location = Point(20, 135), Size = Size(150, 15), ForeColor = Color.White)
        statCaughtSunken <- new Label(Text = "Sunken: 0 (Total: 0)", Location = Point(20, 150), Size = Size(150, 15), ForeColor = Color.White)
        
        this.InitializeConfig()
        
        this.Controls.AddRange([|
            btnToggle; btnUseBait; btnUseLure; btnBaitPos; btnRodPos; btnLurePos;
            statusTotalCaught; statusBaitSet; statusRodSet; statusLureSet;
            lblRodPos; lblPanicKey;
            statCaughtFish; statCaughtTreasure; statCaughtSunken;
            |])
        this.Controls.Add(new Label(Text = "Provided by FlyinC4T at GitHub", Location = Point(0, windowSize.Height - 55), ForeColor = Color.DarkGray, AutoSize = true))

        this.InitializePointer()

        timer.Tick.Add(fun _ -> this.CheckFishIcon())

    member this.InitializeConfig() =
        // Apply loaded config values
        useBait <- config.UseBait
        useLure <- config.useLure
        rodPosition <- Point(config.RodPosX, config.RodPosY)
        lurePosition <- Point(config.LurePosX, config.LurePosY)
        baitPosition <- Point(config.BaitPosX, config.BaitPosY)
        statusCaught.[0] <- config.caughtFish
        statusCaught.[1] <- config.caughtTreasure
        statusCaught.[2] <- config.caughtSunken
    
        // Update UI to reflect loaded config
        if rodPosition <> Point.Empty then
            statusRodSet.Text <- $"{rodPosition.X}, {rodPosition.Y}"
        if lurePosition <> Point.Empty then
            statusLureSet.Text <- $"{lurePosition.X}, {lurePosition.Y}"
        if baitPosition <> Point.Empty then
            statusBaitSet.Text <- $"{baitPosition.X}, {baitPosition.Y}"
    
        btnUseBait.BackColor <- if useBait then Color.Green else Color.Red
        btnUseLure.BackColor <- if useLure then Color.Green else Color.Red
    
        statCaughtFish.Text <- $"Fish: 0 (Total: {statusCaught.[0]})"
        statCaughtTreasure.Text <- $"Treasure: 0 (Total: {statusCaught.[1]})"
        statCaughtSunken.Text <- $"Sunken: 0 (Total: {statusCaught.[2]})"
    
        printfn "Config loaded and applied"

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
            lblMouseCoords.Text <- $"Pixel: ({pos.X}, {pos.Y})"
            mouseLabel.Location <- Point(pos.X - (mouseLabel.Size.Width/2), pos.Y - 50)

            if Control.MouseButtons = MouseButtons.Left then
                Thread.Sleep(100)
                let foregroundWindow = GetForegroundWindow()
        
                if not (foregroundWindow = this.Handle) then
                    if baitPointerActive then
                        this.ToggleBaitPointer()
                        baitPosition <- pos
                        statusBaitSet.Text <- $"{pos.X}, {pos.Y}"
                        printfn $"Bait position set at ({pos.X}, {pos.Y})"
                    elif rodPointerActive then
                        this.ToggleRodPointer()
                        rodPosition <- pos
                        statusRodSet.Text <- $"{pos.X}, {pos.Y}"
                        printfn $"Rod position set at ({pos.X}, {pos.Y})"
                    elif lurePointerActive then
                        this.ToggleLurePointer()
                        lurePosition <- pos
                        statusLureSet.Text <- $"{pos.X}, {pos.Y}"
                        printfn $"Lure position set at ({pos.X}, {pos.Y})"
        )

    member this.ToggleBaitPointer() =
        baitPointerActive <- not baitPointerActive
        if baitPointerActive then
            mouseTracker.Start()
            mouseLabel.Show()
        else
            mouseTracker.Stop()
            mouseLabel.Hide()

    member this.ToggleRodPointer() =
        rodPointerActive <- not rodPointerActive
        if rodPointerActive then
            mouseTracker.Start()
            mouseLabel.Show()
        else
            mouseTracker.Stop()
            mouseLabel.Hide()

    member this.ToggleLurePointer() =
        lurePointerActive <- not lurePointerActive
        if lurePointerActive then
            mouseTracker.Start()
            mouseLabel.Show()
        else
            mouseTracker.Stop()
            mouseLabel.Hide()

    member this.ToggleFishing() =
        if rodPosition = Point.Empty then
            MessageBox.Show("Set Fishing Rod position first.") |> ignore
        else
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
            
    member this.ToggleLure() =
        if lurePosition = Point.Empty then
            MessageBox.Show("Set lure click position first.") |> ignore
        else
            useLure <- not useLure
            btnUseLure.BackColor <- (if useLure then Color.Green else Color.Red) // no ternary is unswag

    member this.GetSearchBounds() =
        let hwnd = GetForegroundWindow()
        let mutable rect = Drawing.Rectangle()
        GetWindowRect(hwnd, &rect) |> ignore
        
        // X: 0.3 to 0.6 (30% width, centered at 0.45)
        let x = int(float rect.X + float rect.Width * 0.3)
        let w = int(float rect.Width * 0.3)  // 0.6 - 0.3 = 0.3 width

        // Y: 0.1 to 0.6 (50% height, starting at 10%)
        let y = int(float rect.Y + float rect.Height * 0.1)
        let h = int(float rect.Height * 0.5)  // 0.6 - 0.1 = 0.5 height

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

                    let (confidence, pos) = findTemplate screenMat tmpl searchBounds.X searchBounds.Y "Fish Prompt"
                
                    match pos with
                    | Some(x, y) ->
                        // Confirm with 3 consecutive checks
                        let mutable confirmCount = 0
                        for i in 1..3 do
                            Thread.Sleep(50)
                            use confirmBmp = captureArea(searchBounds)
                            use confirmMat = bitmapToMat confirmBmp
                            let (_, confirmPos) = findTemplate confirmMat tmpl searchBounds.X searchBounds.Y $"Fish Prompt Confirm {i}"
                            match confirmPos with
                            | Some _ -> confirmCount <- confirmCount + 1
                            | None -> ()
                
                        if confirmCount = 3 then
                            printfn $"Fish confirmed 3/3 times at ({x}, {y})! Starting fishing loop..."
                            timer.Stop()
                            cancelSource <- new CancellationTokenSource()
                            Async.Start(async { do! this.FishingLoop() }, cancelSource.Token)
                        else
                            printfn $"Fish not confirmed (only {confirmCount}/3), ignoring false positive"
                    | None ->
                        ()
                | None ->
                    this.ToggleFishing()

    member this.CheckForCatchNotification() =
        let hwnd = GetForegroundWindow()
        let mutable windowRect = Drawing.Rectangle()
        GetWindowRect(hwnd, &windowRect) |> ignore
    
        // Calculate 550x300 as ratio of 1920x1080
        // 550/1920 ≈ 0.286 width (28.6%)
        // 300/1080 ≈ 0.278 height (27.8%)
    
        let notifWidth = int(float windowRect.Width * 0.286)
        let notifHeight = int(float windowRect.Height * 0.278)
    
        // Position at bottom right
        let notifBounds = Rectangle(
            windowRect.X + windowRect.Width - notifWidth,   // Right edge minus width
            windowRect.Y + windowRect.Height - notifHeight, // Bottom edge minus height
            notifWidth,
            notifHeight
        )
    
        use screenBmp = captureArea(notifBounds)
        use screenMat = bitmapToMat screenBmp
    
        // Check for fish notification
        match notificationTemplateFish with
        | Some tmpl ->
            let (_, pos) = findTemplate screenMat tmpl notifBounds.X notifBounds.Y "Fish"
            match pos with
            | Some _ -> Some "fish"
            | None ->
                match notificationTemplateTreasure with
                | Some treasureTmpl ->
                    let (_, treasurePos) = findTemplate screenMat treasureTmpl notifBounds.X notifBounds.Y "Treasure"
                    match treasurePos with
                    | Some _ -> Some "treasure"
                    | None ->
                        match notificationTemplateSunken with
                        | Some sunkenTmpl ->
                            let (_, sunkenPos) = findTemplate screenMat sunkenTmpl notifBounds.X notifBounds.Y "Sunken"
                            match sunkenPos with
                            | Some _ -> Some "sunken"
                            | None -> None
                        | None -> None
                | None -> None
        | None -> None

    member this.FishingLoop() =
        async {
            let mutable fishingActive = true
            let startTime = DateTime.Now
        
            while fishingActive && not cancelSource.IsCancellationRequested do
                // Just keep clicking
                clickAtScreenRatio(0.75, 0.75)
                do! Async.Sleep(Random().Next(150, 200))
            
                match this.CheckForCatchNotification() with
                | Some catchType ->
                    printfn $"Caught {catchType}!"
                    fishingActive <- false
                    this.Invoke(new Action(fun () -> this.ExpectedFishCaught(catchType))) |> ignore
                | None ->
                    // timeout case
                    if (DateTime.Now - startTime).TotalSeconds > 30.0 then
                        printfn "No notification detected after 30s, assuming fish"
                        fishingActive <- false
                        this.Invoke(new Action(fun () -> this.ExpectedFishCaught("fish"))) |> ignore
            
                do! Async.Sleep(100)
        }

    member this.ExpectedFishCaught(catchType: string) =
        match catchType with
        | "treasure" ->
            statusCaught.[1] <- statusCaught.[1] + 1
            statusCaughtSession.[1] <- statusCaughtSession.[1] + 1
            statCaughtTreasure.Text <- $"Treasure: {statusCaughtSession.[1]} (Total: {statusCaught.[1] + statusCaughtSession.[1]})"
        | "sunken" ->
            statusCaught.[2] <- statusCaught.[2] + 1
            statusCaughtSession.[2] <- statusCaughtSession.[2] + 1
            statCaughtSunken.Text <- $"Sunken: {statusCaughtSession.[2]} (Total: {statusCaught.[2] + statusCaughtSession.[2]})"
        | _ -> // "fish"
            statusCaught.[0] <- statusCaught.[0] + 1
            statusCaughtSession.[0] <- statusCaughtSession.[0] + 1
            statCaughtFish.Text <- $"Fish: {statusCaughtSession.[0]} (Total: {statusCaught.[0] + statusCaughtSession.[0]})"

        totalCaught <- totalCaught + 1
        statusTotalCaught.Text <- $"Caught: {totalCaught}"
        this.FixFishingRod(false)
        
        if isRunning then timer.Start()  // Resume scanning

    member this.FixFishingRod(withLure: bool) =
        lastFixRotation <- DateTime.Now

        if withLure then
            Thread.Sleep(Random().Next(100,150))
            if lurePosition <> Point.Empty then
                clickAt(lurePosition.X, lurePosition.Y)
            clickAtScreenRatio(0.75, 0.75)
            Thread.Sleep(Random().Next(1000,1200))
        else
            clickAt(lurePosition.X, lurePosition.Y) // unselect rod
            Thread.Sleep(500)

        clickAt(rodPosition.X, rodPosition.Y)
        if useBait then 
            clickAt(baitPosition.X, baitPosition.Y)
        clickAtScreenRatio(0.75, 0.75) // default

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