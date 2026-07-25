using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SocketPractice
{
    /// <summary>
    /// 길이 프리픽스 프레이밍 도우미.
    ///
    /// 패킷 형식:  [길이 4바이트][본문 ...]
    ///
    /// 이게 있어야 TCP의 "바이트 흐름"에서 원래 메시지 경계를 복원할 수 있다.
    /// 실제 게임에서는 본문 첫 바이트에 '메시지 타입'을 넣어 확장한다.
    ///   예) [길이][타입][netId][x][y][z] ...
    /// </summary>
    static class MessageIO
    {
        // 이상한 길이가 오면 즉시 끊기 위한 상한 (악의적/버그 패킷 방어)
        const int MaxMessageSize = 1024 * 1024;   // 1MB

        /// <summary>메시지 하나를 [길이][본문] 형태로 전송.</summary>
        public static void Send(NetworkStream stream, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            byte[] header = BitConverter.GetBytes(body.Length);   // int = 4바이트

            // 헤더와 본문을 한 번에 보내면 쪼개질 확률이 줄어든다(성능에도 유리)
            byte[] packet = new byte[4 + body.Length];
            Buffer.BlockCopy(header, 0, packet, 0, 4);
            Buffer.BlockCopy(body, 0, packet, 4, body.Length);

            stream.Write(packet, 0, packet.Length);
        }

        /// <summary>메시지 하나를 정확히 읽어서 돌려준다. 연결이 끊기면 IOException.</summary>
        public static string Receive(NetworkStream stream)
        {
            // ① 길이(4바이트)를 먼저 정확히 읽는다
            byte[] header = ReadExactly(stream, 4);
            int length = BitConverter.ToInt32(header, 0);

            if (length < 0 || length > MaxMessageSize)
                throw new IOException("비정상 메시지 길이: " + length);

            if (length == 0) return "";

            // ② 그 길이만큼만 정확히 읽는다 -> 이게 한 메시지
            byte[] body = ReadExactly(stream, length);
            return Encoding.UTF8.GetString(body);
        }

        /// <summary>
        /// ★ 소켓 프로그래밍의 핵심 함수.
        /// Read는 요청한 만큼 준다는 보장이 없으므로, 다 채울 때까지 반복해야 한다.
        /// 이걸 안 하면 "가끔 패킷이 깨지는" 재현 어려운 버그가 생긴다.
        /// </summary>
        static byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int n = stream.Read(buffer, offset, count - offset);
                if (n <= 0) throw new IOException("연결이 끊겼습니다.");
                offset += n;
            }

            return buffer;
        }
    }
}
