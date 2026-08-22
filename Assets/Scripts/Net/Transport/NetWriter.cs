using System;
using System.Text;

namespace JellyNet
{
    public class NetWriter
    {
        private const int LENGTH_FIELD_SIZE = 4;

        private byte[] buffer;
        private int position;

        public NetWriter(int capacity = 1024)
        {
            buffer = new byte[capacity];
        }

        public byte[] Buffer => buffer;
        public int Length => position;

        public NetWriter Begin(MsgType type)
        {
            position = LENGTH_FIELD_SIZE;
            WriteByte((byte)type);
            return this;
        }

        public void End()
        {
            int bodyLength = position - LENGTH_FIELD_SIZE;

            buffer[0] = (byte)bodyLength;
            buffer[1] = (byte)(bodyLength >> 8);
            buffer[2] = (byte)(bodyLength >> 16);
            buffer[3] = (byte)(bodyLength >> 24);
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            buffer[position++] = value;
        }

        public void WriteInt(int value)
        {
            Ensure(4);
            buffer[position++] = (byte)value;
            buffer[position++] = (byte)(value >> 8);
            buffer[position++] = (byte)(value >> 16);
            buffer[position++] = (byte)(value >> 24);
        }

        public void WriteFloat(float value)
        {
            WriteInt(BitConverter.SingleToInt32Bits(value));
        }

        public void WriteString(string value)
        {
            value ??= string.Empty;

            int byteCount = Encoding.UTF8.GetByteCount(value);

            WriteInt(byteCount);
            Ensure(byteCount);

            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, position);
            position += byteCount;
        }

        private void Ensure(int extra)
        {
            int required = position + extra;

            if (required <= buffer.Length)
                return;

            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, required));
        }
    }
}
