using System;
using System.IO;
using System.Text;

namespace JellyNet
{
    public class NetReader
    {
        private byte[] buffer;
        private int end;
        private int position;

        public int Remaining => end - position;

        public void Reset(byte[] source, int offset, int length)
        {
            buffer = source;
            position = offset;
            end = offset + length;
        }

        public byte ReadByte()
        {
            Need(1);
            return buffer[position++];
        }

        public MsgType ReadMsgType()
        {
            return (MsgType)ReadByte();
        }

        public int ReadInt()
        {
            Need(4);

            int value = buffer[position]
                      | (buffer[position + 1] << 8)
                      | (buffer[position + 2] << 16)
                      | (buffer[position + 3] << 24);
            position += 4;

            return value;
        }

        public float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle(ReadInt());
        }

        public string ReadString()
        {
            int byteCount = ReadInt();

            if (byteCount < 0 || byteCount > NetConfig.MAX_BODY_SIZE)
                throw new IOException($"[ {nameof(NetReader)} ] 문자열 길이 이상 : {byteCount}");

            Need(byteCount);

            string value = Encoding.UTF8.GetString(buffer, position, byteCount);
            position += byteCount;

            return value;
        }

        private void Need(int count)
        {
            if (position + count > end)
                throw new IOException($"[ {nameof(NetReader)} ] 패킷이 짧습니다. 필요 {count}, 남음 {Remaining}");
        }
    }
}
