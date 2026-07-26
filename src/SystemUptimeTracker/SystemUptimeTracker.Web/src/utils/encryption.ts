import crypto from "crypto";

const algorithm = "aes-256-gcm";
const encryptionKeyLength = 32;
const initializationVectorLength = 12;
const authTagLength = 16;
const payloadVersion = "v1";
const payloadIvLabel = "Impersonation payload IV";
const payloadAuthTagLabel = "Impersonation payload auth tag";

function parseRequiredHexBuffer(
  envVarName: string,
  value: string,
  expectedLength: number,
) {
  if (value.length !== expectedLength * 2 || !/^[0-9a-f]+$/iu.test(value)) {
    throw new Error(
      `${envVarName} must be a ${expectedLength * 8}-bit hexadecimal value.`,
    );
  }

  const buffer = Buffer.from(value, "hex");

  if (buffer.length !== expectedLength) {
    throw new Error(
      `${envVarName} must decode to exactly ${expectedLength} bytes.`,
    );
  }

  return buffer;
}

function getEncryptionSettings() {
  const keyHex = process.env.IMPERSONATE_ENCRYPTION_KEY;

  if (!keyHex) {
    throw new Error(
      "IMPERSONATE_ENCRYPTION_KEY must be configured for impersonation encryption.",
    );
  }

  return {
    key: parseRequiredHexBuffer(
      "IMPERSONATE_ENCRYPTION_KEY",
      keyHex,
      encryptionKeyLength,
    ),
  };
}

function serializePayload(value: unknown) {
  return typeof value === "string" ? value : JSON.stringify(value);
}

function parsePayload(value: string) {
  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}

function encrypt(value: unknown) {
  const { key } = getEncryptionSettings();
  const iv = crypto.randomBytes(initializationVectorLength);
  const cipher = crypto.createCipheriv(algorithm, key, iv);
  const plaintext = serializePayload(value);
  let encrypted = cipher.update(plaintext, "utf8", "hex");
  encrypted += cipher.final("hex");
  const authTag = cipher.getAuthTag();

  return [
    payloadVersion,
    iv.toString("hex"),
    authTag.toString("hex"),
    encrypted,
  ].join(":");
}

function decrypt(encryptedText) {
  const { key } = getEncryptionSettings();
  const [version, ivHex, authTagHex, ciphertext] = encryptedText.split(":");

  if (
    version !== payloadVersion ||
    !ivHex ||
    !authTagHex ||
    !ciphertext ||
    ivHex.length !== initializationVectorLength * 2 ||
    authTagHex.length !== authTagLength * 2
  ) {
    throw new Error("Encrypted impersonation payload is invalid.");
  }

  const iv = parseRequiredHexBuffer(
    payloadIvLabel,
    ivHex,
    initializationVectorLength,
  );
  const authTag = parseRequiredHexBuffer(
    payloadAuthTagLabel,
    authTagHex,
    authTagLength,
  );
  const decipher = crypto.createDecipheriv(algorithm, key, iv);
  decipher.setAuthTag(authTag);
  let decrypted = decipher.update(ciphertext, "hex", "utf8");
  decrypted += decipher.final("utf8");
  return parsePayload(decrypted);
}

const newKeys = async () => {
  const key = crypto.randomBytes(32);

  return {
    key: key.toString("hex"),
  };
};

export { encrypt, decrypt, newKeys };
