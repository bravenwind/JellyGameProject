using System;
using System.Text;

namespace JellyNet
{
    /// <summary>
    /// 메시지를 바이트로 조립하는 도구.
    ///
    /// 사용법:
    ///   writer.Begin(MsgType.Chat);
    ///   writer.WriteString("안녕");
    ///   writer.End();
    ///   conn.Send(writer);          // Buffer[0 .. Length) 를 그대로 전송
    ///
    /// ★ 인스턴스를 재사용한다(매번 new 하지 않는다).
    ///   그래야 byte[] 할당이 없어 GC 압박이 생기지 않는다.
    /// </summary>
    public class NetWriter
    {
        byte[] _buf;
        int _pos;

        public NetWriter(int capacity = 1024)
        {
            _buf = new byte[capacity];
        }

        public byte[] Buffer { get { return _buf; } }
        public int Length { get { return _pos; } }

        /// <summary>새 메시지 시작. 앞 4바이트는 길이 자리로 비워둔다.</summary>
        public NetWriter Begin(MsgType type)
        {
            _pos = 4;                  // 0~3번은 길이 필드 자리
            WriteByte((byte)type);
            return this;
        }

        /// <summary>메시지 완성. 비워뒀던 앞 4바이트에 본문 길이를 채운다.</summary>
        public void End()
        {
            int bodyLen = _pos - 4;    // 길이 필드 자신은 제외
            _buf[0] = (byte)bodyLen;
            _buf[1] = (byte)(bodyLen >> 8);
            _buf[2] = (byte)(bodyLen >> 16);
            _buf[3] = (byte)(bodyLen >> 24);
        }

        // ── 기본 타입 쓰기 (리틀 엔디언: 낮은 자리 먼저) ──

        public void WriteByte(byte v)
        {
            Ensure(1);
            _buf[_pos++] = v;
        }

        public void WriteInt(int v)
        {
            Ensure(4);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
            _buf[_pos++] = (byte)(v >> 16);
            _buf[_pos++] = (byte)(v >> 24);
        }

        public void WriteShort(short v)
        {
            Ensure(2);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
        }

        /// <summary>float를 4바이트로. 비트를 그대로 int로 보고 기록한다.</summary>
        public void WriteFloat(float v)
        {
            // BitConverter.GetBytes(float)는 매번 byte[4]를 새로 만들어 GC를 유발하므로
            // 비트 패턴만 꺼내 WriteInt로 넘긴다(할당 0).
            WriteInt(BitConverter.SingleToInt32Bits(v));
        }

        /// <summary>문자열: [바이트수 4][UTF-8 바이트들]</summary>
        public void WriteString(string s)
        {
            if (s == null) s = "";
            int n = Encoding.UTF8.GetByteCount(s);
            WriteInt(n);
            Ensure(n);
            // 배열에 직접 써서 중간 byte[] 할당을 피한다
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _pos);
            _pos += n;
        }

        void Ensure(int extra)
        {
            if (_pos + extra <= _buf.Length) return;
            int need = _pos + extra;
            int cap = _buf.Length * 2;
            if (cap < need) cap = need;
            Array.Resize(ref _buf, cap);   // 버퍼가 부족하면 두 배로 늘린다
        }
    }
}
