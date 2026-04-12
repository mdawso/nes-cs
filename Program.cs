namespace nes;

using Raylib_cs;

public class Program
{
    static void Main(string[] args)
    {
        Raylib.InitWindow(256 * 2, 240 * 2, "nes");
        Raylib.SetTargetFPS(60);

        Bus bus = new Bus();

        if (args.Length > 0)
        {
            Cartridge cart = new Cartridge(args[0]);
            bus.InsertCartridge(cart);
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

            Raylib.DrawTextureEx(screenTexture, new System.Numerics.Vector2(0, 0), 0.0f, 2.0f, Color.White);

            Raylib.EndDrawing();
        }

        Raylib.UnloadTexture(screenTexture);
        Raylib.UnloadImage(screenImage);
        Raylib.CloseWindow();
    }
}