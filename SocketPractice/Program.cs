using System;
using System.Text;

namespace SocketPractice
{
    /// <summary>
    /// B-1단계 실습: 콘솔에서 TCP를 직접 다뤄본다.
    ///
    /// 실행법 (터미널 2개를 띄워서 하나는 호스트, 하나는 클라로):
    ///   dotnet run -- step1-host
    ///   dotnet run -- step1-client 127.0.0.1
    /// </summary>
    static class Program
    {
        static void Main(string[] args)
        {
            // 윈도우 콘솔에서 한글이 깨지지 않도록
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string ip = args.Length > 1 ? args[1] : "127.0.0.1";

            switch (mode)
            {
                case "step1-host": Step1Echo.RunHost(); break;
                case "step1-client": Step1Echo.RunClient(ip); break;

                case "step2-host": Step2Framing.RunHost(); break;
                case "step2-client": Step2Framing.RunClient(ip); break;

                case "step3-host": Step3MultiClient.RunHost(); break;
                case "step3-client": Step3MultiClient.RunClient(ip); break;

                default: PrintUsage(); break;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("=== TCP 소켓 실습 (젤리팡 아일랜드 LAN 마이그레이션 1단계) ===");
            Console.WriteLine();
            Console.WriteLine("터미널 2개를 열어서 하나는 호스트, 하나는 클라로 실행하세요.");
            Console.WriteLine();
            Console.WriteLine("  [1단계] 프레이밍 없는 에코 - 메시지가 뭉치는 현상을 직접 확인");
            Console.WriteLine("     dotnet run -- step1-host");
            Console.WriteLine("     dotnet run -- step1-client 127.0.0.1");
            Console.WriteLine();
            Console.WriteLine("  [2단계] 길이 프리픽스 적용 - 뭉쳐도 정확히 복원되는지 확인");
            Console.WriteLine("     dotnet run -- step2-host");
            Console.WriteLine("     dotnet run -- step2-client 127.0.0.1");
            Console.WriteLine();
            Console.WriteLine("  [3단계] 다중 접속 + 호스트 판정 - 우리 게임 구조의 축소판");
            Console.WriteLine("     dotnet run -- step3-host");
            Console.WriteLine("     dotnet run -- step3-client 127.0.0.1     (여러 개 띄워보세요)");
            Console.WriteLine();
            Console.WriteLine("다른 기기에서 접속할 때는 127.0.0.1 대신 호스트 PC의 IP를 넣으세요.");
            Console.WriteLine("(윈도우: ipconfig -> IPv4 주소, 예: 192.168.0.5)");
        }
    }
}
