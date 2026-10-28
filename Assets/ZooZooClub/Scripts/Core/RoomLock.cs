using System;
using System.Text;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 방에 들어온 학생의 4자리 비밀번호(PIN)로 서버 저장의 "열쇠"를 만든다.
    /// 서버에는 PIN이 아니라 이 열쇠 이름 아래에 저장하므로, PIN을 모르면 그 학생의 저장을 읽을 수도 고칠 수도 없다.
    /// 열쇠 = SHA-256("zoo|방코드|이름키|PIN")의 앞 32글자(16진수).
    /// </summary>
    public static class RoomLock
    {
        public const int PinLength = 4;

        /// <summary>숫자 4자리면 그대로, 아니면 null.</summary>
        public static string CleanPin(string pin)
        {
            if (pin == null) return null;
            pin = pin.Trim();
            if (pin.Length != PinLength) return null;
            foreach (char c in pin)
            {
                if (c < '0' || c > '9') return null;
            }
            return pin;
        }

        public static string For(string roomCode, string nameKey, string pin)
        {
            string hex = Sha256Hex("zoo|" + (roomCode ?? "") + "|" + (nameKey ?? "") + "|" + (pin ?? ""));
            return hex.Substring(0, 32);
        }

        // ---------- SHA-256 (브라우저에서도 같은 결과가 나오도록 직접 계산한다) ----------

        static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        static uint Rot(uint x, int n) { return (x >> n) | (x << (32 - n)); }

        public static string Sha256Hex(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            long bitLength = (long)data.Length * 8;
            int padded = ((data.Length + 9 + 63) / 64) * 64;
            var msg = new byte[padded];
            Array.Copy(data, msg, data.Length);
            msg[data.Length] = 0x80;
            for (int i = 0; i < 8; i++) msg[padded - 1 - i] = (byte)(bitLength >> (8 * i));

            uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
            uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
            var w = new uint[64];
            for (int chunk = 0; chunk < padded; chunk += 64)
            {
                for (int i = 0; i < 16; i++)
                {
                    int j = chunk + i * 4;
                    w[i] = ((uint)msg[j] << 24) | ((uint)msg[j + 1] << 16) | ((uint)msg[j + 2] << 8) | msg[j + 3];
                }
                for (int i = 16; i < 64; i++)
                {
                    uint s0 = Rot(w[i - 15], 7) ^ Rot(w[i - 15], 18) ^ (w[i - 15] >> 3);
                    uint s1 = Rot(w[i - 2], 17) ^ Rot(w[i - 2], 19) ^ (w[i - 2] >> 10);
                    w[i] = unchecked(w[i - 16] + s0 + w[i - 7] + s1);
                }
                uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
                for (int i = 0; i < 64; i++)
                {
                    uint S1 = Rot(e, 6) ^ Rot(e, 11) ^ Rot(e, 25);
                    uint ch = (e & f) ^ (~e & g);
                    uint t1 = unchecked(h + S1 + ch + K[i] + w[i]);
                    uint S0 = Rot(a, 2) ^ Rot(a, 13) ^ Rot(a, 22);
                    uint maj = (a & b) ^ (a & c) ^ (b & c);
                    uint t2 = unchecked(S0 + maj);
                    h = g; g = f; f = e; e = unchecked(d + t1); d = c; c = b; b = a; a = unchecked(t1 + t2);
                }
                h0 = unchecked(h0 + a); h1 = unchecked(h1 + b); h2 = unchecked(h2 + c); h3 = unchecked(h3 + d);
                h4 = unchecked(h4 + e); h5 = unchecked(h5 + f); h6 = unchecked(h6 + g); h7 = unchecked(h7 + h);
            }
            var sb = new StringBuilder(64);
            foreach (uint v in new[] { h0, h1, h2, h3, h4, h5, h6, h7 }) sb.Append(v.ToString("x8"));
            return sb.ToString();
        }
    }
}
