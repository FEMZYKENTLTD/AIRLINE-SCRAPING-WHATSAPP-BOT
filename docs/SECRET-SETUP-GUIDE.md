# Secret Setup Guide

Step-by-step instructions for obtaining and configuring every required secret.

---

## 1. WhatsApp Business API

### 1.1 WHATSAPP_ACCESS_TOKEN

**What**: Token used to send messages via Meta Cloud API.  
**Where to get**: [Facebook Developer Portal](https://developers.facebook.com/apps)

**Steps**:
1. Go to https://developers.facebook.com
2. Log in with your Facebook account
3. Select your app (or create one: "Create App" → "Business" type)
4. Go to "WhatsApp" → "API Setup" in the left sidebar
5. Under "Temporary access token", click "Generate token"
6. Copy the token (starts with `EAAE...`)
7. Note: Temporary tokens expire in 24 hours. For production, generate a permanent token via Business Manager.

**Environment variable**: `WHATSAPP_ACCESS_TOKEN`  
**Where to store**:
- Local: `.env` file → `WHATSAPP_ACCESS_TOKEN=your_token_here`
- GitHub: Repository → Settings → Secrets and variables → Actions → `WHATSAPP_ACCESS_TOKEN`

**Verification**: The token should work when calling `https://graph.facebook.com/v24.0/{phone_number_id}/messages`

**Common errors**:
- Token expired: Regenerate a new one
- 401 Unauthorized: Token doesn't have `whatsapp_business_messaging` permission
- 403 Forbidden: App not approved for WhatsApp Business API

**⚠️ SECURITY**: Previous WhatsApp credentials were exposed in Git history. **Rotate all tokens immediately**.

### 1.2 WHATSAPP_PHONE_NUMBER_ID

**What**: The WhatsApp Business phone number identifier.  
**Where to get**: Same page as access token (WhatsApp → API Setup → "From" phone number).

**Steps**:
1. In the Facebook Developer Portal, go to your app
2. Navigate to WhatsApp → API Setup
3. Find the "From" phone number section
4. Copy the Phone number ID (numeric, e.g., `123456789012345`)

**Environment variable**: `WHATSAPP_PHONE_NUMBER_ID`  
**Verification**: Should be a numeric string (15-17 digits)

### 1.3 WHATSAPP_VERIFY_TOKEN

**What**: A string you choose to verify webhook requests from Meta.  
**How to configure**: You create this yourself.

**Steps**:
1. Choose any random string (e.g., `my_wh_verify_2026_abc123`)
2. Set it in your `.env` file
3. In Facebook Developer Portal → WhatsApp → Configuration → Webhook
4. Enter your webhook URL: `https://your-domain.com/webhook`
5. Enter the SAME verify token you put in `.env`
6. Click "Verify and save"

**Environment variable**: `WHATSAPP_VERIFY_TOKEN`  
**Verification**: When Meta sends a GET request to your webhook, your server echoes back the challenge if the token matches.

### 1.4 WHATSAPP_APP_SECRET

**What**: Your Facebook App's secret key, used for webhook signature verification.  
**Where to get**: Facebook Developer Portal → Settings → Basic → App Secret (click "Show")

**Steps**:
1. Go to your app's dashboard
2. Click "Settings" → "Basic"
3. Find "App Secret" and click "Show"
4. Copy the value

**Environment variable**: `WHATSAPP_APP_SECRET`  
**Verification**: Webhooks with invalid signatures will be rejected (401).

---

## 2. Telegram Bot

### 2.1 TELEGRAM_BOT_TOKEN

**What**: Token used to control your Telegram bot.  
**Where to get**: [@BotFather](https://t.me/BotFather) on Telegram

**Steps**:
1. Open Telegram and search for @BotFather
2. Send `/newbot`
3. Follow the prompts to name your bot
4. BotFather will give you a token like `123456:ABCdefGHIjklMNO-pqrSTUvwxyz`
5. Copy this token

**Environment variable**: `TELEGRAM_BOT_TOKEN`  
**Where to store**: `.env` → `TELEGRAM_BOT_TOKEN=your_token_here`  
**Verification**: `https://api.telegram.org/bot{TOKEN}/getMe` should return your bot info.

**Common errors**:
- 401 Unauthorized: Token is invalid or revoked
- Bot not responding: Webhook not set or incorrect URL

### 2.2 TELEGRAM_WEBHOOK_SECRET

**What**: A secret string you choose to validate incoming Telegram webhooks.  
**How to configure**: You create this yourself.

**Steps**:
1. Generate a random string: `openssl rand -hex 32`
2. Set in `.env` → `TELEGRAM_WEBHOOK_SECRET=your_random_string`
3. When setting the webhook (see below), pass this as `secret_token`

**Setting the webhook**:
```bash
curl -X POST "https://api.telegram.org/bot{TOKEN}/setWebhook" \
  -d "url=https://your-domain.com/telegram" \
  -d "secret_token=YOUR_SECRET"
```

**Verification**: Incoming Telegram requests will have the header `X-Telegram-Bot-Api-Secret-Token` matching your secret.

---

## 3. Azure OpenAI

### 3.1 AZURE_OPENAI_ENDPOINT

**What**: Your Azure OpenAI resource endpoint URL.  
**Where to get**: [Azure Portal](https://portal.azure.com)

**Steps**:
1. Go to https://portal.azure.com
2. Navigate to your Azure OpenAI resource (or create one: "Create a resource" → "Azure OpenAI")
3. In the resource's left menu, click "Keys and Endpoint"
4. Copy the "Endpoint" value (format: `https://your-resource.openai.azure.com/`)

**Environment variable**: `AZURE_OPENAI_ENDPOINT`  
**Verification**: URL should end with `.openai.azure.com`

**Note**: Azure OpenAI requires an approved subscription. Standard OpenAI accounts do not have access.

### 3.2 AZURE_OPENAI_API_KEY

**What**: API key for authenticating with your Azure OpenAI resource.  
**Where to get**: Same page as endpoint (Keys and Endpoint → KEY 1 or KEY 2)

**Steps**:
1. In your Azure OpenAI resource → Keys and Endpoint
2. Copy KEY 1 (or KEY 2)

**Environment variable**: `AZURE_OPENAI_API_KEY`  
**Common errors**:
- 401: Wrong key or the resource was regenerated
- 429: Rate limit exceeded

### 3.3 AZURE_OPENAI_DEPLOYMENT

**What**: The name of your model deployment in Azure OpenAI Studio.  
**Where to get**: Azure OpenAI Studio → Deployments

**Steps**:
1. Go to https://oai.azure.com (Azure OpenAI Studio)
2. Click "Deployments" in the left menu
3. Find your deployment (or create one: "Create new deployment" → select model like `gpt-4o-mini`)
4. Copy the "Deployment name" (NOT the model name)

**Environment variable**: `AZURE_OPENAI_DEPLOYMENT`  
**Default**: `gpt-4.1-mini` (update to match your actual deployment name)

**Common errors**:
- "Deployment not found": The deployment name doesn't match. Check exact spelling.
- "Model not available": You need to request access to the model in your Azure subscription.

---

## 4. JWT Authentication

### 4.1 JWT_SECRET

**What**: A random string used to sign JWT tokens. Must be at least 32 characters.  
**How to generate**:
```bash
openssl rand -base64 32
```

**Environment variable**: `JWT_SECRET`  
**Where to store**:
- Local: `.env` → `JWT_SECRET=your_generated_secret`
- GitHub: Repository secrets → `JWT_SECRET`

**Verification**: After starting the app, call `/api/auth/login` with valid credentials and receive a JWT token.

**⚠️ WARNING**: Never commit this value to source control. Never share it. Rotate if compromised.

### 4.2 JWT_ISSUER

**What**: Identifies who issued the token.  
**Default**: `AirlineServiceManagement`  
**Environment variable**: `JWT_ISSUER`  
**Configuration**: Set in `.env` or leave default.

### 4.3 JWT_AUDIENCE

**What**: Identifies the intended recipient of the token.  
**Default**: `AirlineServiceManagement`  
**Environment variable**: `JWT_AUDIENCE`  
**Configuration**: Must match the value used in token validation.

---

## 5. Admin Credentials

### 5.1 ADMIN_USERNAME

**What**: Username for admin API login.  
**Default**: `admin`  
**Environment variable**: `ADMIN_USERNAME`  
**Configuration**: Set in `.env` → `ADMIN_USERNAME=admin`

### 5.2 ADMIN_PASSWORD

**What**: Password for admin API login.  
**How to choose**: Use a strong password (12+ characters, mixed case, numbers, symbols).  
**Environment variable**: `ADMIN_PASSWORD`  
**Where to store**:
- Local: `.env` → `ADMIN_PASSWORD=your_strong_password`
- GitHub: Repository secrets → `ADMIN_PASSWORD`

**Verification**: POST to `/api/auth/login` with `{"username":"admin","password":"your_password"}`

**⚠️ SECURITY**: The application currently compares this as plaintext from the environment variable. This is acceptable for development/internal use. For production, consider implementing password hashing.

---

## 6. Amadeus (Flight API)

### 6.1 Creating an Amadeus Developer Account

1. Go to https://developers.amadeus.com
2. Click "Register" or "Sign Up"
3. Complete registration with your email
4. Verify your email address
5. Log in to the dashboard

### 6.2 Creating a New Application

1. In the Amadeus dashboard, click "My Apps" in the top menu
2. Click "Create New App"
3. Enter an app name (e.g., "Airline Service Platform")
4. Select the APIs you need:
   - **Flight Offers Search** (required for pricing)
   - **Flight Create Orders** (optional, for booking)
5. Click "Create"
6. You'll see your app's credentials

### 6.3 AMADEUS_CLIENT_ID

**What**: Your Amadeus API key (client ID).  
**Where to find**: My Apps → Your App → API Key  
**Environment variable**: `AMADEUS_CLIENT_ID`  
**Format**: Alphanumeric string (e.g., `A1B2C3D4E5F6G7H8`)

### 6.4 AMADEUS_CLIENT_SECRET

**What**: Your Amadeus API secret (client secret).  
**Where to find**: My Apps → Your App → API Secret  
**Environment variable**: `AMADEUS_CLIENT_SECRET`  
**Format**: Alphanumeric string

### 6.5 AMADEUS_BASE_URL

**What**: The Amadeus API base URL.  
**Test/Sandbox**: `https://test.api.amadeus.com` (default, use this for development)  
**Production**: `https://api.amadeus.com` (use only when ready for production)  
**Environment variable**: `AMADEUS_BASE_URL`

### 6.6 Testing Amadeus Authentication

```bash
curl -X POST "https://test.api.amadeus.com/v1/security/oauth2/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=YOUR_ID&client_secret=YOUR_SECRET"
```

Successful response includes `"access_token": "..."`.

### 6.7 Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| 401 Unauthorized | Wrong client ID or secret | Double-check credentials |
| 403 Forbidden | API not enabled for your app | Enable "Flight Offers Search" in My Apps |
| Invalid client | Client ID/secret mismatch | Regenerate credentials |
| No results | Test environment has limited data | Use valid IATA codes; test API has fewer routes |

**Note**: The test API environment has limited flight data. Not all routes/prices will return results.

---

## 7. Database

### 7.1 DATABASE_CONNECTION_STRING

**What**: Connection string for the SQLite database.  
**Default**: `Data Source=whatsappbot.db` (file-based, in the application directory)  
**Environment variable**: `DATABASE_CONNECTION_STRING`

**Local development**: Leave as default (`Data Source=whatsappbot.db`).  
**Docker**: Use `Data Source=/data/whatsappbot.db` with a volume mount.  
**GitHub Actions**: Not needed (SQLite file created at runtime).

---

## 8. GitHub Repository Secrets Setup

To configure secrets in GitHub:

1. Go to your repository on GitHub
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Add each secret with the NAME and VALUE:

| Secret Name | Required | Source |
|------------|----------|--------|
| `WHATSAPP_ACCESS_TOKEN` | For WhatsApp | Facebook Developer Portal |
| `WHATSAPP_PHONE_NUMBER_ID` | For WhatsApp | Facebook Developer Portal |
| `WHATSAPP_VERIFY_TOKEN` | For WhatsApp | You choose |
| `WHATSAPP_APP_SECRET` | For WhatsApp | Facebook Developer Portal |
| `TELEGRAM_BOT_TOKEN` | For Telegram | @BotFather |
| `TELEGRAM_WEBHOOK_SECRET` | For Telegram | You generate |
| `AZURE_OPENAI_ENDPOINT` | For AI | Azure Portal |
| `AZURE_OPENAI_API_KEY` | For AI | Azure Portal |
| `AZURE_OPENAI_DEPLOYMENT` | For AI | Azure OpenAI Studio |
| `AMADEUS_CLIENT_ID` | For flights | Amadeus Developer Portal |
| `AMADEUS_CLIENT_SECRET` | For flights | Amadeus Developer Portal |
| `JWT_SECRET` | For admin auth | You generate (openssl rand -base64 32) |
| `ADMIN_USERNAME` | For admin auth | You choose |
| `ADMIN_PASSWORD` | For admin auth | You choose |
| `DATABASE_CONNECTION_STRING` | For database | Default: `Data Source=whatsappbot.db` |

---

## 9. Configuration Verification Checklist

After configuring all secrets, verify each:

- [ ] WhatsApp webhook GET verification returns challenge
- [ ] WhatsApp can send a test message via API
- [ ] Telegram bot responds to `/start`
- [ ] Azure OpenAI returns a chat completion
- [ ] Amadeus returns an OAuth token
- [ ] Amadeus returns flight offers for a test route (e.g., LOS→LHR)
- [ ] Admin login returns a JWT token
- [ ] Health endpoint returns "Healthy"
- [ ] Database table creation succeeds on startup
