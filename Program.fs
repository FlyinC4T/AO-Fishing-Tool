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
extern void mouse_event(uint32 dwFlags, uint32 dx, uint32 dy, uint32 dwData, int dwExtraInfo)

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
        
        if maxVal > 0.3 then
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

type MainForm() as this =
    inherit Form()
    
    let mutable isRunning = false
    let mutable pointerActive = false
    let mutable fishCaught : int32 = 0
    let mutable statusLabel : Control = null
    let mutable statusCaught : Control = null
    let mutable mouseLabel : Form = null
    let mutable mouseTracker : Timer = null  // Add this field
    let mutable template: Mat option = None // bobber icon
    let mutable lastFixRotation = DateTime.Now



    let timer = new Timer(Interval = 200)
    
    do
        this.FormBorderStyle <- FormBorderStyle.SizableToolWindow
        this.StartPosition <- FormStartPosition.CenterScreen
        this.TopMost <- true
        this.Text <- "AO Fishing Tool"
        this.Size <- Size(210, 220)
        this.BackColor <- Drawing.Color.Red
        this.TransparencyKey <- Drawing.Color.Red  // Makes lime pixels transparent
        this.Opacity <- 1.0
        
        let btnToggle : Control = new Button(Text = "Toggle", Location = Point(20, 10), Size = Size(100, 20), ForeColor = Color.Cyan)
        btnToggle.Click.Add(fun _ -> this.ToggleFishing())

        let btnLoad : Control = new Button(Text = "Load Image", Location = Point(20, 30), Size = Size(100, 20), ForeColor = Color.Cyan)
        btnLoad.Click.Add(fun _ -> this.LoadTemplate())

        let btnPointer : Control = new Button(Text = "Pointer", Location = Point(20, 50), Size = Size(100, 20), ForeColor = Color.Cyan)
        btnPointer.Click.Add(fun _ -> this.TogglePointer())

        statusLabel <- new Label(Text = "Status: Off", Location = Point(20, 70), Size = Size(200, 15), ForeColor = Color.Cyan)
        statusCaught <- new Label(Text = "Fish Caught: 0", Location = Point(20, 85), Size = Size(200, 15), ForeColor = Color.Cyan)

        this.Controls.AddRange([| btnToggle; btnLoad; btnPointer; statusLabel; statusCaught |])
        
        this.InitializePointer()

        timer.Tick.Add(fun _ -> this.CheckFishIcon())
    
    member this.InitializePointer() =
        mouseLabel <- new Form(
            FormBorderStyle = FormBorderStyle.None,
            BackColor = Drawing.Color.Black,
            ForeColor = Drawing.Color.Lime,
            TopMost = true,
            ShowInTaskbar = false,
            Size = Size(200, 50),
            StartPosition = FormStartPosition.Manual,
            Opacity = 0.9
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
            let ratioX = float pos.X / float screenBounds.Width
            let ratioY = float pos.Y / float screenBounds.Height
    
            lblMouseCoords.Text <- $"Pixel: ({pos.X}, {pos.Y})\nRatio: ({ratioX:F3}, {ratioY:F3})"
            mouseLabel.Location <- Point(pos.X + 15, pos.Y - 60)
        )

    member this.TogglePointer() =
        pointerActive <- not pointerActive
        if pointerActive then
            mouseTracker.Start()
            mouseLabel.Show()
        else
            mouseTracker.Stop()
            mouseLabel.Hide()

    member this.ToggleFishing() =
        isRunning <- not isRunning
        statusLabel.Text <- "Status: " + (if isRunning then "On" else "Off") // no ternary is unswag

        if isRunning then
            timer.Start()
            printfn "Fishing started at %A" DateTime.Now
        if not isRunning then
            timer.Stop()
            printfn "Fishing stopped at %A" DateTime.Now
        
    member this.LoadTemplate() =
        use ofd = new OpenFileDialog(Filter = "Images|*.png;*.jpg;*.bmp", Title = "Select fish icon")
        if ofd.ShowDialog() = DialogResult.OK then
            template <- Some(new Mat(ofd.FileName))
            //MessageBox.Show("Template loaded!") |> ignore

    member this.CheckFishIcon() =
        let foregroundWindow = GetForegroundWindow()
        if isRunning && foregroundWindow <> this.Handle then
            if (DateTime.Now - lastFixRotation).TotalSeconds > 120.0 then
                this.FixFishingRod(true)
                printfn "Dropping lure..."
            else
                match template with
                | Some tmpl ->
                    let searchBounds = this.Bounds
                    use screenBmp = captureArea(searchBounds)
                    use screenMat = bitmapToMat screenBmp
        
                    match findTemplate screenMat tmpl searchBounds.X searchBounds.Y with
                    | Some(x, y) ->
                        printfn $"Found fish icon at ({x}, {y})! Starting fishing loop..."
                        timer.Stop()  // Pause main timer
                        Async.Start(async{do! this.FishingLoop()})
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
        
        while fishingActive do
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
                        printfn "Icon disappeared, continuing to reel for 2 more seconds..."
                        iconDisappeared <- true
                        disappearTime <- DateTime.Now
                    
                    // Keep clicking for 2 seconds after icon disappears
                    let timeSinceDisappear = (DateTime.Now - disappearTime).TotalSeconds
                    if timeSinceDisappear < 3.0 then
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
            clickAtScreenRatio(0.56, 0.93) // lure
            clickAtScreenRatio(0.75, 0.75)
            Thread.Sleep(Random().Next(1500,2000))
        else
            clickAtScreenRatio(0.6, 0.93) // rod -- unselecting to reselect
            
        clickAtScreenRatio(0.6, 0.93) // rod
        clickAtScreenRatio(0.5, 0.776) // giant bait
        clickAtScreenRatio(0.75, 0.75)

        

[<STAThread>]
[<EntryPoint>]
let main argv =
    Application.EnableVisualStyles()
    Application.Run(new MainForm())
    0