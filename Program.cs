namespace nes;

using Raylib_cs;
using System.Numerics;
public class Program
{
    static void Main(string[] args)
    {

        const int initialScale = 2;

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(256 * initialScale, 240 * initialScale, "nes");
        Raylib.SetWindowMonitor(0);
        Raylib.SetTargetFPS(60);

        Bus bus = new Bus();

        string err_text = string.Empty;

        if (args.Length < 1) err_text = "No ROM Loaded.";
        else {
            try
            {
                Cartridge cart = new Cartridge(args[0]);
                bus.InsertCartridge(cart);
                Console.WriteLine($"Loaded ROM: {args[0]}");
                Console.WriteLine($"Using Mapper: {bus.cartridge?.Mapper}");
            } catch (Exception e)
            {
                err_text = e.Message;
            }
        }

        Image screenImage = Raylib.GenImageColor(256, 240, Color.Black);
        Texture2D screenTexture = Raylib.LoadTextureFromImage(screenImage);

        while (!Raylib.WindowShouldClose())
        {
            byte state = 0;
            if (Raylib.IsKeyDown(KeyboardKey.Z)) state |= 1 << 0;
            if (Raylib.IsKeyDown(KeyboardKey.X)) state |= 1 << 1;
            if (Raylib.IsKeyDown(KeyboardKey.RightShift)) state |= 1 << 2;
            if (Raylib.IsKeyDown(KeyboardKey.Enter)) state |= 1 << 3;
            if (Raylib.IsKeyDown(KeyboardKey.Up)) state |= 1 << 4;
            if (Raylib.IsKeyDown(KeyboardKey.Down)) state |= 1 << 5;
            if (Raylib.IsKeyDown(KeyboardKey.Left)) state |= 1 << 6;
            if (Raylib.IsKeyDown(KeyboardKey.Right)) state |= 1 << 7;

            bus.controllerState = state;

            bus.RunFrame();
            bus.ppu.frameComplete = false;

            unsafe
            {
                fixed (uint* ptr = bus.ppu.screenBuffer)
                {
                    Raylib.UpdateTexture(screenTexture, ptr);
                }
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            float scale = Math.Min((float)Raylib.GetScreenWidth() / 256, (float)Raylib.GetScreenHeight() / 240);
            float offsetX = (Raylib.GetScreenWidth() - (256 * scale)) / 2.0f;
            float offsetY = (Raylib.GetScreenHeight() - (240 * scale)) / 2.0f;

            Raylib.DrawTextureEx(screenTexture, new Vector2(offsetX, offsetY), 0.0f, scale, Color.White);

            if (err_text != string.Empty) Raylib.DrawText(err_text, 10, 10, 24, Color.Red);

            Raylib.EndDrawing();
        }


        bus.cartridge?.SaveSram();
        
        Raylib.UnloadTexture(screenTexture);
        Raylib.UnloadImage(screenImage);
        Raylib.CloseWindow();
    }
}