# Troubleshooting

## Build Issues

### "dotnet restore" fails
- Ensure .NET 8 SDK is installed: `dotnet --version`
- Check NuGet sources: `dotnet nuget list source`
- Clear NuGet cache: `dotnet nuget locals all --clear`

### "dotnet build" errors
- Check all using statements are correct
- Ensure project references are valid
- Verify NuGet package versions are compatible

## Database Issues

### "SQLite Error: no such table"
- Ensure `EnsureCreated()` or migrations have run
- Check connection string in `.env` or `appsettings.json`
- Delete `whatsappbot.db` and restart (development only)

### Database locked errors
- Ensure no other process is using the database file
- Check file permissions

## WhatsApp Issues

### Webhook verification fails
- Verify `WHATSAPP_VERIFY_TOKEN` matches Meta dashboard
- Ensure endpoint is publicly accessible (use ngrok for local dev)
- Check GET request returns the challenge string

### Messages not received
- Verify webhook is subscribed to `messages` event
- Check `WHATSAPP_ACCESS_TOKEN` is valid and not expired
- Ensure HTTPS endpoint is configured
- Check application logs for errors

### Signature verification fails
- Verify `WHATSAPP_APP_SECRET` is correct
- Ensure request body is not modified before verification

## Telegram Issues

### Bot not responding
- Verify `TELEGRAM_BOT_TOKEN` is valid
- Check webhook is set correctly
- Ensure `/telegram` endpoint is accessible
- Check application logs

### "Bad Request" from Telegram API
- Check message format (Markdown syntax errors)
- Ensure chat_id is valid
- Verify message length (4096 char limit)

## AI Issues

### "AI is not configured correctly"
- Verify `AZURE_OPENAI_ENDPOINT` is set
- Verify `AZURE_OPENAI_API_KEY` is valid
- Check `AZURE_OPENAI_DEPLOYMENT` matches your deployment name
- Ensure the Azure OpenAI resource is active

### AI responses are slow
- Check Azure OpenAI region latency
- Reduce `LLM_MAX_TOKENS` for faster responses
- Monitor Azure OpenAI dashboard for throttling

## Session Issues

### Session expired unexpectedly
- Default timeout is 7 days (10080 minutes)
- Check `SESSION_TIMEOUT_MINUTES` configuration
- Verify `LastActivityAtUtc` is being updated

### Session state lost after restart
- WhatsApp uses in-memory sessions (lost on restart)
- Telegram uses persistent sessions (survives restarts)

## Amadeus Issues

### Flight search returns no results
- Verify `AMADEUS_CLIENT_ID` and `AMADEUS_CLIENT_SECRET`
- Ensure using test API URL: `https://test.api.amadeus.com`
- Check Amadeus developer dashboard for API status
- Verify IATA codes are valid
