using System.Globalization;
using SkiaSharp;


namespace DesktopAppDemo.Utils
{
    public static class ScreenParamsHelper
    {
        public static bool TryParse(string inputScreen, out string? message, out SKRect? rectangle)
        {
            message = null;
            rectangle = null;
            if (string.IsNullOrWhiteSpace(inputScreen))
            {
                message = "屏幕范围参数不能为空";
                return false;
            }
            string[] param = inputScreen.Split(',', System.StringSplitOptions.TrimEntries);
            if (param.Length != 4 && param.Length != 2)
            {
                message = "录制范围格式不正确，请输入 x,y 或 x,y,width,height，例如：0,0 或 0,0,800,600";
                return false;
            }
            if (!int.TryParse(param[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                message = "录制范围的 x、y 必须为整数像素坐标";
                return false;
            }
            int width = 0, height = 0;
            bool hasSize = param.Length == 4 && (param[2].Length > 0 || param[3].Length > 0);
            if (hasSize && (!int.TryParse(param[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                || !int.TryParse(param[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
                || width < 0 || height < 0 || (width == 0) != (height == 0)))
            {
                message = "width、height 必须同时为正整数，或同时留空/为 0 以自动确定录制范围";
                return false;
            }
            var parsed = SKRect.Create(x, y, width, height);
            if ((double)parsed.Left != x || (double)parsed.Top != y
                || (double)parsed.Width != width || (double)parsed.Height != height
                || (long)width * height > int.MaxValue / 4)
            {
                message = "录制范围过大，无法表示为有效的像素矩形";
                return false;
            }
            rectangle = parsed;
            return true;
        }
    }
}
