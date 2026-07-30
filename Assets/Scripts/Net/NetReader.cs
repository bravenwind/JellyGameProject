using System;
using System.IO;
using System.Text;

namespace JellyNet
{
    /// <summary>
    /// 받은 바이트에서 값을 꺼내는 도구. NetWriter와 정확히 반대 순서로 읽어야 한다.
    ///
    /// ★ 복사하지 않는다(zero-copy).
    ///   수신 누적 버퍼의 일부 구간을 그대로 가리키기만 하므로 할당이 없다.
    ///   대신 핸들러가 끝난 뒤에는 내용이 무효가 되므로, 나중에 쓸 값은 미리 꺼내둬야 한다.
    /// </summary>
    public class NetReader
    {
        byte[] _buf;
        int _end;     // 이 메시지의 끝(배열 인덱스)
        int _pos;     // 현재 읽는 위치

        /// <summary>버퍼의 [offset, offset+length) 구간을 이 메시지로 삼는다.</summary>
        public void Reset(byte[] buf, int offset, int length)
        {
            _buf = buf;
            _pos = offset;
            _end = offset + length;
        }

        public int Remaining { get { return _end - _pos; } }

        public byte ReadByte()
        {
            Need(1);
            return _buf[_pos++];
        }

        public MsgType ReadMsgType()
        {
            return (MsgType)ReadByte();
        }

        public int ReadInt()
        {
            Need(4);
            int v = _buf[_pos]
                  | (_buf[_pos + 1] << 8)
                  | (_buf[_pos + 2] << 16)
                  | (_buf[_pos + 3] << 24);
            _pos += 4;
            return v;
        }

        public short ReadShort()
        {
            Need(2);
            short v = (short)(_buf[_pos] | (_buf[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle(ReadInt());
        }

        public string ReadString()
        {
            int n = ReadInt();
            if (n < 0 || n > NetConfig.MaxBodySize) throw new IOException("문자열 길이 이상: " + n);
            Need(n);
            string s = Encoding.UTF8.GetString(_buf, _pos, n);
            _pos += n;
            return s;
        }

        /// <summary>
        /// 남은 바이트가 부족하면 예외. 깨진 패킷이 왔을 때 조용히 이상 동작하는 대신
        /// 즉시 터뜨려서 상위(FramedConnection)가 연결을 끊게 한다.
        /// </summary>
        void Need(int n)
        {
            if (_pos + n > _end)
                throw new IOException("패킷이 짧습니다. 필요 " + n + ", 남음 " + Remaining);
        }
    }
}
