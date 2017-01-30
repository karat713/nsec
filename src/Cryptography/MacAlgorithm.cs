using System;
using System.Diagnostics;
using static Interop.Libsodium;

namespace NSec.Cryptography
{
    //
    //  A message authentication code (MAC) algorithm
    //
    //  Examples
    //
    //      | Algorithm    | Reference       | Key Size | Nonce Size | Customization | MAC Size  |
    //      | ------------ | --------------- | -------- | ---------- | ------------- | --------  |
    //      | AES-CMAC     | RFC 4493        | 16       | 0          | no            | 16        |
    //      | BLAKE2b      | RFC 7693        | 0..64    | 0          | no            | 1..64     |
    //      | HMAC-SHA-256 | RFC 2104        | any      | 0          | no            | 32        |
    //      | HMAC-SHA-512 | RFC 2104        | any      | 0          | no            | 64        |
    //      | KMAC128      | NIST SP 800-185 | any      | 0          | yes           | any       |
    //      | KMAC256      | NIST SP 800-185 | any      | 0          | yes           | any       |
    //      | KMACXOF128   | NIST SP 800-185 | any      | 0          | yes           | any       |
    //      | KMACXOF256   | NIST SP 800-185 | any      | 0          | yes           | any       |
    //      | Poly1305-AES | [1]             | 32       | 16         | no            | 16        |
    //      | UMAC-AES     | RFC 4418        | 16,24,32 | 1..16      | no            | 4,8,12,16 |
    //
    //      [1] http://cr.yp.to/mac.html
    //
    public abstract class MacAlgorithm : Algorithm
    {
        private readonly int _defaultKeySize;
        private readonly int _defaultMacSize;
        private readonly int _maxKeySize;
        private readonly int _maxMacSize;
        private readonly int _minKeySize;
        private readonly int _minMacSize;

        internal MacAlgorithm(
            int minKeySize,
            int defaultKeySize,
            int maxKeySize,
            int minMacSize,
            int defaultMacSize,
            int maxMacSize)
        {
            Debug.Assert(minKeySize > 0);
            Debug.Assert(defaultKeySize >= minKeySize);
            Debug.Assert(maxKeySize >= defaultKeySize);
            Debug.Assert(minMacSize > 0);
            Debug.Assert(defaultMacSize >= minMacSize);
            Debug.Assert(maxMacSize >= defaultMacSize);

            _minKeySize = minKeySize;
            _defaultKeySize = defaultKeySize;
            _maxKeySize = maxKeySize;
            _minMacSize = minMacSize;
            _defaultMacSize = defaultMacSize;
            _maxMacSize = maxMacSize;
        }

        public int DefaultKeySize => _defaultKeySize;

        public int DefaultMacSize => _defaultMacSize;

        public int MaxKeySize => _maxKeySize;

        public int MaxMacSize => _maxMacSize;

        public int MinKeySize => _minKeySize;

        public int MinMacSize => _minMacSize;

        public byte[] Sign(
            Key key,
            ReadOnlySpan<byte> data)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Algorithm != this)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(key));

            byte[] mac = new byte[_defaultMacSize];
            SignCore(key.Handle, data, mac);
            return mac;
        }

        public byte[] Sign(
            Key key,
            ReadOnlySpan<byte> data,
            int macSize)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Algorithm != this)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(key));
            if (macSize < _minMacSize)
                throw new ArgumentOutOfRangeException(nameof(macSize));
            if (macSize > _maxMacSize)
                throw new ArgumentOutOfRangeException(nameof(macSize));

            byte[] mac = new byte[macSize];
            SignCore(key.Handle, data, mac);
            return mac;
        }

        public void Sign(
            Key key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Algorithm != this)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(key));
            if (mac.Length < _minMacSize)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(mac));
            if (mac.Length > _maxMacSize)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(mac));

            SignCore(key.Handle, data, mac);
        }

        public bool TryVerify(
            Key key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Algorithm != this)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(key));
            if (mac.Length < _minMacSize)
                return false;
            if (mac.Length > _maxMacSize)
                return false;

            // crypto_auth_hmacsha{256,512}_verify does not support truncated
            // HMACs, so we calculate the MAC ourselves and call sodium_memcmp
            // to compare the expected MAC with the actual MAC.

            // SignCore can output a truncated MAC. However, truncation requires
            // a copy. So we provide an array with the default length and
            // compare only the initial 'mac.Length' bytes.

            Span<byte> temp;
            try
            {
                unsafe
                {
                    int length = Math.Max(mac.Length, _defaultMacSize);
                    byte* pointer = stackalloc byte[length];
                    temp = new Span<byte>(pointer, length);
                }

                SignCore(key.Handle, data, temp);

                Debug.Assert(mac.Length <= temp.Length);
                int error = sodium_memcmp(ref temp.DangerousGetPinnableReference(), ref mac.DangerousGetPinnableReference(), (UIntPtr)mac.Length);
                return error == 0;
            }
            finally
            {
                sodium_memzero(ref temp.DangerousGetPinnableReference(), (UIntPtr)temp.Length);
            }
        }

        public void Verify(
            Key key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Algorithm != this)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(key));
            if (mac.Length < _minMacSize)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(mac));
            if (mac.Length > _maxMacSize)
                throw new ArgumentException(Error.ArgumentExceptionMessage, nameof(mac));

            // crypto_auth_hmacsha{256,512}_verify does not support truncated
            // HMACs, so we calculate the MAC ourselves and call sodium_memcmp
            // to compare the expected MAC with the actual MAC.

            // SignCore can output a truncated MAC. However, truncation requires
            // a copy. So we provide an array with the default length and
            // compare only the initial 'mac.Length' bytes.

            Span<byte> temp;
            try
            {
                unsafe
                {
                    int length = Math.Max(mac.Length, _defaultMacSize);
                    byte* pointer = stackalloc byte[length];
                    temp = new Span<byte>(pointer, length);
                }

                SignCore(key.Handle, data, temp);

                Debug.Assert(mac.Length <= temp.Length);
                int error = sodium_memcmp(ref temp.DangerousGetPinnableReference(), ref mac.DangerousGetPinnableReference(), (UIntPtr)mac.Length);
                if (error != 0)
                {
                    throw new CryptographicException();
                }
            }
            finally
            {
                sodium_memzero(ref temp.DangerousGetPinnableReference(), (UIntPtr)temp.Length);
            }
        }

        internal abstract void SignCore(
            SecureMemoryHandle keyHandle,
            ReadOnlySpan<byte> data,
            Span<byte> mac);
    }
}
