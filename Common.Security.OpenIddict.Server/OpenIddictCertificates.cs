using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Common.Security.OpenIddict.Server;

/// <summary>
/// Пара сертификатов, которыми OpenIddict подписывает и шифрует токены.
/// </summary>
/// <param name="Signing">Сертификат подписи access- и identity-токенов.</param>
/// <param name="Encryption">Сертификат шифрования данных, которые не должны читаться клиентом.</param>
public sealed record OpenIddictCertificates(
    X509Certificate2 Signing,
    X509Certificate2 Encryption);

/// <summary>
/// Загружает или создаёт постоянные сертификаты OpenIddict.
/// </summary>
/// <remarks>
/// <para>
/// В разработке OpenIddict использует временные ключи в памяти: после перезапуска
/// приложения выданные ранее токены становятся недействительными. В контейнере
/// перезапуск происходит часто, поэтому ключи нужно хранить на диске.
/// </para>
/// <para>
/// Каталог задаётся <see cref="OpenIddictServerOptions.CertificatesDirectory"/>.
/// При первом запуске создаются два самоподписанных сертификата, при следующих —
/// они читаются с диска. Это сертификаты для домашней локальной сети, а не для
/// выхода в интернет: закрытый ключ лежит на постоянном томе, пароль — в секретах
/// приложения. Для публичного размещения выдавайте сертификаты доверенным центром.
/// </para>
/// </remarks>
public static class OpenIddictCertificateLoader
{
    private const string SigningFileName = "signing.pfx";
    private const string EncryptionFileName = "encryption.pfx";

    /// <summary>
    /// Возвращает сертификаты, если каталог задан; иначе <c>null</c>.
    /// </summary>
    /// <param name="options">Настройки сервера.</param>
    /// <param name="contentRootPath">Корень приложения для относительного пути.</param>
    /// <exception cref="InvalidOperationException">
    /// Каталог задан, но пароль пуст или файл сертификата повреждён.
    /// </exception>
    public static OpenIddictCertificates? LoadOrCreate(
        OpenIddictServerOptions options,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        if (string.IsNullOrWhiteSpace(options.CertificatesDirectory))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.CertificatesPassword))
        {
            throw new InvalidOperationException(
                "Задан OpenIddict:Certificates:Directory, но не задан OpenIddict:Certificates:Password.");
        }

        var directory = Path.IsPathRooted(options.CertificatesDirectory)
            ? options.CertificatesDirectory
            : Path.Combine(contentRootPath, options.CertificatesDirectory);

        Directory.CreateDirectory(directory);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var signing = LoadOrCreate(
            Path.Combine(directory, SigningFileName),
            "OpenIddict signing",
            options.CertificatesPassword,
            X509KeyUsageFlags.DigitalSignature);

        var encryption = LoadOrCreate(
            Path.Combine(directory, EncryptionFileName),
            "OpenIddict encryption",
            options.CertificatesPassword,
            X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment);

        return new OpenIddictCertificates(signing, encryption);
    }

    private static X509Certificate2 LoadOrCreate(
        string path,
        string subject,
        string password,
        X509KeyUsageFlags keyUsage)
    {
        if (!File.Exists(path))
        {
            CreateSelfSigned(path, subject, password, keyUsage);
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                $"Не удалось прочитать сертификат OpenIddict '{path}'. " +
                "Проверьте пароль OpenIddict:Certificates:Password или удалите файл, чтобы создать новый.",
                exception);
        }
    }

    private static void CreateSelfSigned(
        string path,
        string subject,
        string password,
        X509KeyUsageFlags keyUsage)
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={subject}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(keyUsage, critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
