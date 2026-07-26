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

                case "step3-host":
                    // "dotnet run -- step3-host quiet" 로 켜면 메시지 로그를 끈다(부하 테스트용)
                    Step3MultiClient.RunHost(args.Length > 1 && args[1].ToLowerInvariant() == "quiet");
                    break;
                case "step3-client": Step3MultiClient.RunClient(ip); break;

                case "step4-load":
                    {
                        int cCount = args.Length > 2 ? int.Parse(args[2]) : 4;      // 가상 클라 수
                        int msgs = args.Length > 3 ? int.Parse(args[3]) : 200;    // 클라당 메시지 수
                        int interval = args.Length > 4 ? int.Parse(args[4]) : 10;     // 전송 간격(ms)
                        LoadTest.Run(ip, cCount, msgs, interval);
                        break;
                    }

                case "step5-game":
                    {
                        int players = args.Length > 2 ? int.Parse(args[2]) : 8;    // 플레이어 수
                        int hz = args.Length > 3 ? int.Parse(args[3]) : 20;   // 초당 전송 횟수
                        int secs = args.Length > 4 ? int.Parse(args[4]) : 10;   // 지속 시간(초)
                        int bytes = args.Length > 5 ? int.Parse(args[5]) : 24;   // 패킷 크기
                        LoadTest.RunGame(ip, players, hz, secs, bytes);
                        break;
                    }

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
            Console.WriteLine("  [4단계] 호스트 부하 테스트 - 가상 클라 N명이 한꺼번에 공격");
            Console.WriteLine("     dotnet run -- step3-host quiet                    (로그 끄고 호스트 실행)");
            Console.WriteLine("     dotnet run -- step4-load 127.0.0.1 8 500 5");
            Console.WriteLine("                              IP  클라수 메시지수 간격ms");
            Console.WriteLine("     * 간격을 0으로 주면 최대 속도로 쏟아붓습니다.");
            Console.WriteLine();
            Console.WriteLine("  [5단계] 게임 조건 테스트 - 정확한 주기(Hz)로 실제 게임처럼");
            Console.WriteLine("     dotnet run -- step3-host quiet");
            Console.WriteLine("     dotnet run -- step5-game 127.0.0.1 8 20 10 24");
            Console.WriteLine("                               IP  인원 Hz 초 바이트");
            Console.WriteLine("     * 위 예시 = 8명이 초당 20번, 10초간, 24바이트 위치 패킷");
            Console.WriteLine();
            Console.WriteLine("다른 기기에서 접속할 때는 127.0.0.1 대신 호스트 PC의 IP를 넣으세요.");
            Console.WriteLine("(윈도우: ipconfig -> IPv4 주소, 예: 192.168.0.5)");
        }
    }
}
