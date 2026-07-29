using HB_Claude.Models;
using MySqlConnector;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace HB_Claude.Services;

// =====================================================================
//  Datalagret mot databasdesign v2 (matchmate_se_db_V2).
//  Nya tabeller: app_user, user_delegate, team, player, game,
//  game_event, event_type, phase, zone.
//  SQL:en alias:ar tillbaka nya kolumnnamn till de namn som modellerna
//  och Razor-sidorna redan använder, så app-ytan är oförändrad.
// =====================================================================

public class ApiService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ApiService> _logger;
    private readonly string ConnStr;
    private readonly string _salt;

    public ApiService(IConfiguration config, ILogger<ApiService> logger)
    {
        _config = config;
        _logger = logger;
        ConnStr = config.GetConnectionString("MatchMate")
            ?? throw new InvalidOperationException("ConnectionStrings:MatchMate saknas i konfigurationen.");
        _salt = config["Auth:Salt"]
            ?? throw new InvalidOperationException("Auth:Salt saknas i konfigurationen.");
    }

    // ---- Auth / User ----

    public static string Hash256(string password, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password + salt);
        var hash = SHA256.HashData(bytes);
        var result = Convert.ToBase64String(hash);
        return result.Replace("+", "_").Replace("/", "_");
    }

    public static string RandomString()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }

    public static string RandPwd()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = new Random();
        return new string(Enumerable.Range(0, 8).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }

    private static string ComputeMd5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private async Task<int> MMM_Exists(User usr)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT count(*) as ant FROM app_user WHERE replace(email,'+','') = @uid;", connection);
        cmd.Parameters.AddWithValue("@uid", usr.UserName);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return int.TryParse(reader["ant"].ToString(), out var n) ? n : 0;
    }

    public async Task<bool> FinnsUser(User usr) => await MMM_Exists(usr) != 0;

    public async Task<User?> GetUser(string username, string password)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id AS UserID, email AS UserName, password_hash AS Password, role AS typ, status " +
            "FROM app_user WHERE replace(email,'+','') = @uid AND password_hash=@pwd;", connection);
        cmd.Parameters.AddWithValue("@uid", username);
        cmd.Parameters.AddWithValue("@pwd", password);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new User
        {
            UserID = reader["UserID"].ToString() == "30" ? "4" : reader["UserID"].ToString() ?? "",
            UserName = reader["UserName"].ToString() ?? "",
            Password = reader["Password"].ToString() ?? "",
            typ = reader["typ"].ToString() ?? "",
            status = reader["status"].ToString() ?? ""
        };
    }

    public async Task<User?> GetUserNew(string username, string password)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id AS UserID, email AS UserName, password_hash AS Password, role AS typ, status " +
                "FROM app_user WHERE replace(email,'+','') = @uid AND password_hash=@pwd;", connection);
            cmd.Parameters.AddWithValue("@uid", username);
            cmd.Parameters.AddWithValue("@pwd", ComputeMd5(password + _salt));
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new User
            {
                UserID = reader["UserID"].ToString() ?? "",
                UserName = reader["UserName"].ToString() ?? "",
                Password = reader["Password"].ToString() ?? "",
                typ = reader["typ"].ToString() ?? "",
                status = reader["status"].ToString() ?? ""
            };
        }
        catch { return null; }
    }

    public async Task<User?> GetUserFromHash(string hash)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT IFNULL(UU.delegate_user_id, U.id) AS UserID, U.email AS UserName, U.password_hash AS Password, " +
            "U.role AS typ, U.status AS status " +
            "FROM app_user U LEFT JOIN user_delegate UU ON U.id=UU.user_id WHERE U.password_hash=@hsh;", connection);
        cmd.Parameters.AddWithValue("@hsh", hash);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new User
        {
            UserID = reader["UserID"].ToString() ?? "",
            UserName = reader["UserName"].ToString() ?? "",
            Password = reader["Password"].ToString() ?? "",
            typ = reader["typ"].ToString() ?? "",
            status = reader["status"].ToString() ?? ""
        };
    }

    public Task<string> GetHash(string pwd)
    {
        return Task.FromResult(ComputeMd5(pwd + _salt));
    }

    public async Task<bool> NewUser(User user)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO app_user (email, password_hash, role, status) VALUES (@UserName, @Password, @typ, @status);", connection);
        cmd.Parameters.AddWithValue("@UserName", user.UserName.Replace("+", ""));
        cmd.Parameters.AddWithValue("@Password", user.Password);
        cmd.Parameters.AddWithValue("@typ", user.typ);
        cmd.Parameters.AddWithValue("@status", user.status);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> NewPwd(User user)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_user SET password_hash=@Password WHERE id=@UserID;", connection);
        cmd.Parameters.AddWithValue("@UserID", user.UserID);
        cmd.Parameters.AddWithValue("@Password", user.Password);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> NewPwdEP(User user)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_user SET password_hash=@Password WHERE replace(email,'+','')=@UserName;", connection);
        cmd.Parameters.AddWithValue("@UserName", user.UserName);
        cmd.Parameters.AddWithValue("@Password", user.Password);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    // ---- Lag (Teams) ----

    private async Task<List<Lag>> GetTeams(string userId)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT name AS Namn, series AS Serie, home_venue AS HemmaHall, category AS KM, id AS Lag_ID " +
            "FROM team WHERE user_id=@userID;", connection);
        cmd.Parameters.AddWithValue("@userID", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Lag>();
        while (await reader.ReadAsync())
            list.Add(new Lag
            {
                Namn = reader["Namn"].ToString() ?? "",
                Serie = reader["Serie"].ToString() ?? "",
                HemmaHall = reader["HemmaHall"].ToString() ?? "",
                KM = reader["KM"].ToString() ?? "",
                LagID = reader["Lag_ID"].ToString() ?? ""
            });
        return list;
    }

    public async Task<List<Lag>?> GetLagFromUser(string userId)
    {
        try { return await GetTeams(userId); }
        catch { return null; }
    }

    public async Task<bool> AddLag(Lag lag)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO team (name, home_venue, series, user_id, category) VALUES (@Namn, @HemmaHall, @Serie, @UserID, @KM);", connection);
        cmd.Parameters.AddWithValue("@Namn", lag.Namn);
        cmd.Parameters.AddWithValue("@HemmaHall", lag.HemmaHall);
        cmd.Parameters.AddWithValue("@Serie", lag.Serie);
        cmd.Parameters.AddWithValue("@UserID", lag.UserID);
        cmd.Parameters.AddWithValue("@KM", lag.KM);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> EditLag(Lag lag)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE team SET name=@Namn, home_venue=@HemmaHall, series=@Serie, category=@KM WHERE id=@LagID;", connection);
        cmd.Parameters.AddWithValue("@Namn", lag.Namn);
        cmd.Parameters.AddWithValue("@HemmaHall", lag.HemmaHall);
        cmd.Parameters.AddWithValue("@Serie", lag.Serie);
        cmd.Parameters.AddWithValue("@KM", lag.KM);
        cmd.Parameters.AddWithValue("@LagID", lag.LagID);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> DelLag(Lag lag)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM team WHERE id=@LagID;", connection);
        cmd.Parameters.AddWithValue("@LagID", lag.LagID);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<string> GetLagNamn(string userId, string lagId)
    {
        var lag = await GetLagFromUser(userId);
        return lag?.FirstOrDefault(l => l.LagID == lagId)?.Namn ?? "";
    }

    // ---- Spelare (Players) ----

    public async Task<List<Spelare>?> GetListaOfSpelareEJ_MX(string lagId)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id AS SpelarID, team_id AS LagID, first_name AS Förnamn, last_name AS Efternamn, position AS Position, " +
                "CASE WHEN alt_shirt_number IS NULL THEN shirt_number ELSE alt_shirt_number END AS Nummer " +
                "FROM player WHERE team_id=@LagID " +
                "ORDER BY CASE WHEN position='MV' THEN 0 ELSE 1 END, " +
                "CASE WHEN alt_shirt_number IS NULL THEN shirt_number ELSE alt_shirt_number END ASC;", connection);
            cmd.Parameters.AddWithValue("@LagID", lagId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Spelare>();
            while (await reader.ReadAsync())
                list.Add(new Spelare
                {
                    SpelareID = reader["SpelarID"].ToString() ?? "",
                    LagID = reader["LagID"].ToString() ?? "",
                    Förnamn = reader["Förnamn"].ToString() ?? "",
                    Efternamn = reader["Efternamn"].ToString() ?? "",
                    Position = reader["Position"].ToString() ?? "",
                    Nummer = reader["Nummer"].ToString() ?? ""
                });
            return list;
        }
        catch { return null; }
    }

    public async Task<List<Spelare>?> GetListaOfSpelare(string lagId)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id AS SpelarID, team_id AS LagID, first_name AS Förnamn, last_name AS Efternamn, position AS Position, " +
                "alt_shirt_number AS XNummer, shirt_number AS Nummer FROM player WHERE team_id=@LagID;", connection);
            cmd.Parameters.AddWithValue("@LagID", lagId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Spelare>();
            while (await reader.ReadAsync())
                list.Add(new Spelare
                {
                    SpelareID = reader["SpelarID"].ToString() ?? "",
                    LagID = reader["LagID"].ToString() ?? "",
                    Förnamn = reader["Förnamn"].ToString() ?? "",
                    Efternamn = reader["Efternamn"].ToString() ?? "",
                    Position = reader["Position"].ToString() ?? "",
                    Nummer = reader["Nummer"].ToString() ?? "",
                    XNummer = reader["XNummer"] == DBNull.Value ? "" : reader["XNummer"].ToString() ?? ""
                });
            return list;
        }
        catch { return null; }
    }

    public async Task<List<Spelare>?> GetSpelarefromID(string spelareId)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id AS SpelarID, team_id AS LagID, first_name AS Förnamn, last_name AS Efternamn, position AS Position, " +
                "CASE WHEN alt_shirt_number IS NULL THEN shirt_number ELSE alt_shirt_number END AS Nummer " +
                "FROM player WHERE id=@SpelareID;", connection);
            cmd.Parameters.AddWithValue("@SpelareID", spelareId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Spelare>();
            while (await reader.ReadAsync())
                list.Add(new Spelare
                {
                    SpelareID = reader["SpelarID"].ToString() ?? "",
                    LagID = reader["LagID"].ToString() ?? "",
                    Förnamn = reader["Förnamn"].ToString() ?? "",
                    Efternamn = reader["Efternamn"].ToString() ?? "",
                    Position = reader["Position"].ToString() ?? "",
                    Nummer = reader["Nummer"].ToString() ?? ""
                });
            return list;
        }
        catch { return null; }
    }

    public async Task<List<Spelare>?> GetSpelareXfromID(string spelareId)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id AS SpelarID, team_id AS LagID, first_name AS Förnamn, last_name AS Efternamn, position AS Position, " +
                "alt_shirt_number AS XNummer, shirt_number AS Nummer FROM player WHERE id=@SpelareID;", connection);
            cmd.Parameters.AddWithValue("@SpelareID", spelareId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Spelare>();
            while (await reader.ReadAsync())
                list.Add(new Spelare
                {
                    SpelareID = reader["SpelarID"].ToString() ?? "",
                    LagID = reader["LagID"].ToString() ?? "",
                    Förnamn = reader["Förnamn"].ToString() ?? "",
                    Efternamn = reader["Efternamn"].ToString() ?? "",
                    Position = reader["Position"].ToString() ?? "",
                    Nummer = reader["Nummer"].ToString() ?? "",
                    XNummer = reader["XNummer"] == DBNull.Value ? "" : reader["XNummer"].ToString() ?? ""
                });
            return list;
        }
        catch { return null; }
    }

    public async Task<bool> AddPlayer(Spelare spelare)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO player (last_name, first_name, team_id, shirt_number, position) VALUES (@Efternamn, @Förnamn, @LagID, @Nummer, @Position);", connection);
        cmd.Parameters.AddWithValue("@Efternamn", spelare.Efternamn);
        cmd.Parameters.AddWithValue("@Förnamn", spelare.Förnamn);
        cmd.Parameters.AddWithValue("@LagID", spelare.LagID);
        cmd.Parameters.AddWithValue("@Nummer", spelare.Nummer);
        cmd.Parameters.AddWithValue("@Position", spelare.Position);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> UpdateSpelare(Spelare spelare)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        // Extranummer är nu infällt i player.alt_shirt_number (ingen separat tabell).
        await using var cmd = new MySqlCommand(
            "UPDATE player SET last_name=@Efternamn, first_name=@Förnamn, team_id=@LagID, shirt_number=@Nummer, " +
            "position=@Position, alt_shirt_number=@Alt WHERE id=@SpelareID;", connection);
        cmd.Parameters.AddWithValue("@Efternamn", spelare.Efternamn);
        cmd.Parameters.AddWithValue("@Förnamn", spelare.Förnamn);
        cmd.Parameters.AddWithValue("@LagID", spelare.LagID);
        cmd.Parameters.AddWithValue("@Nummer", spelare.Nummer);
        cmd.Parameters.AddWithValue("@Position", spelare.Position);
        cmd.Parameters.AddWithValue("@Alt", spelare.XNummer.Length > 0 ? spelare.XNummer : DBNull.Value);
        cmd.Parameters.AddWithValue("@SpelareID", spelare.SpelareID);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> DelPlayer(string spelareId)
    {
        try
        {
            await using var connection = new MySqlConnection(ConnStr);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand("DELETE FROM player WHERE id=@SpelarID;", connection);
            cmd.Parameters.AddWithValue("@SpelarID", spelareId);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ---- Matcher (Matches) ----

    public async Task<List<Match>?> GetAllMatcher(string lagId)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT team_id AS LagID, id AS MatchID, CAST(played_on AS CHAR(10)) AS Datum, opponent AS Motståndare, " +
            "venue AS Plats, status AS Status FROM game WHERE team_id=@LagID;", connection);
        cmd.Parameters.AddWithValue("@LagID", lagId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Match>();
        while (await reader.ReadAsync())
            list.Add(new Match
            {
                LagID = reader["LagID"].ToString() ?? "",
                MatchID = reader["MatchID"].ToString() ?? "",
                Motståndare = reader["Motståndare"].ToString() ?? "",
                Datum = reader["Datum"].ToString() ?? "",
                Plats = reader["Plats"].ToString() ?? "",
                Status = reader["Status"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<Match>> GetMatchInfoEJ_Avslutade(string lagId)
    {
        var all = await GetAllMatcher(lagId);
        return all?.Where(m => m.Status != "Avslutad").ToList() ?? new();
    }

    public async Task<List<Match>> GetMatchInfoAvslutade(string lagId)
    {
        var all = await GetAllMatcher(lagId);
        if (all == null) return new();
        return all.Where(m => m.Status == "Avslutad")
                  .Select(m => { m.Titel = $"{m.Datum} - {m.Motståndare}"; return m; })
                  .ToList();
    }

    public async Task<Match?> GetMatch(string lagId, string matchId)
    {
        var all = await GetAllMatcher(lagId);
        return all?.FirstOrDefault(m => m.MatchID == matchId);
    }

    public async Task<string> AddMatch(Match match)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO game (played_on, team_id, opponent, venue) VALUES (@Datum, @LagID, @Motståndare, @Plats);", connection);
        cmd.Parameters.AddWithValue("@Datum", match.Datum);
        cmd.Parameters.AddWithValue("@LagID", match.LagID);
        cmd.Parameters.AddWithValue("@Motståndare", match.Motståndare);
        cmd.Parameters.AddWithValue("@Plats", match.Plats);
        await cmd.ExecuteNonQueryAsync();
        return cmd.LastInsertedId.ToString();
    }

    public async Task<bool> SetMatchStatus(string matchId, string status)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE game SET status=@Status WHERE id=@MatchID;", connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        cmd.Parameters.AddWithValue("@Status", status);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<int> GetMaxTid(string matchId)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT max(seconds) maxtid FROM game_event WHERE game_id=@matchID;", connection);
        cmd.Parameters.AddWithValue("@matchID", matchId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    // ---- Händelser (Events) ----

    public async Task<bool> AddHändelse(Händelse händelse, string? clientEventId = null)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        // client_event_id (offline-synk) gör inserten idempotent: en omsynkad rad blir
        // en no-op i stället för dubblett. NULL för online-inserts (matchar aldrig UNIQUE).
        await using var cmd = new MySqlCommand(
            "INSERT INTO game_event (seconds, game_id, player_id, event_type_id, client_event_id) " +
            "VALUES (@Tids, @MatchID, @SpelareID, @HändelseID, @Cid) " +
            "ON DUPLICATE KEY UPDATE client_event_id = client_event_id;", connection);
        cmd.Parameters.AddWithValue("@Tids", händelse.Tids);
        cmd.Parameters.AddWithValue("@MatchID", händelse.MatchID);
        cmd.Parameters.AddWithValue("@SpelareID", händelse.SpelareID);
        cmd.Parameters.AddWithValue("@HändelseID", händelse.HändelseID);
        cmd.Parameters.AddWithValue("@Cid", string.IsNullOrEmpty(clientEventId) ? DBNull.Value : clientEventId);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<List<EventsTyp>?> AddMultiHändelse3(Händelse händelse, string? clientEventId = null)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();

        string hanID;
        await using (var cmd1 = new MySqlCommand(
            "SELECT CASE (SELECT COUNT(id) FROM event_type WHERE `text`=@uid) WHEN 0 THEN 0 ELSE (SELECT id FROM event_type WHERE `text`=@uid) END FROM event_type LIMIT 1;", connection))
        {
            cmd1.Parameters.AddWithValue("@uid", händelse.Händelsen);
            hanID = (await cmd1.ExecuteScalarAsync())?.ToString() ?? "0";
        }

        // client_event_id (offline-synk) gör inserten idempotent – se AddHändelse.
        await using (var cmd2 = new MySqlCommand(
            "INSERT INTO game_event (seconds, game_id, player_id, event_type_id, phase_id, zone_id, client_event_id) " +
            "VALUES (@Tids, @MatchID, @SpelareID, @HändelseID, @Fas, @Zon, @Cid) " +
            "ON DUPLICATE KEY UPDATE client_event_id = client_event_id;", connection))
        {
            cmd2.Parameters.AddWithValue("@Tids", händelse.Tids);
            cmd2.Parameters.AddWithValue("@MatchID", händelse.MatchID);
            cmd2.Parameters.AddWithValue("@SpelareID", händelse.SpelareID);
            cmd2.Parameters.AddWithValue("@HändelseID", hanID);
            cmd2.Parameters.AddWithValue("@Fas", händelse.Fas);
            cmd2.Parameters.AddWithValue("@Zon", händelse.Zon);
            cmd2.Parameters.AddWithValue("@Cid", string.IsNullOrEmpty(clientEventId) ? DBNull.Value : clientEventId);
            await cmd2.ExecuteNonQueryAsync();
        }

        const string sql = "SELECT TIME_FORMAT(SEC_TO_TIME(H.seconds),'%i:%s') Tid, " +
            "concat(S.shirt_number,' ',S.last_name) AS namn, H.phase_id Fas, Z.name Zon, " +
            "CASE WHEN HL.`text` LIKE 'Uppst%' THEN 'Uppst' WHEN HL.`text` LIKE 'Fas1%' THEN 'Fas1' WHEN HL.`text` LIKE 'Fas2%' THEN 'Fas2' " +
            "WHEN HL.`text` LIKE 'TeknisktFel%' THEN 'TeknisktFel' WHEN HL.`text` LIKE 'Straff%' THEN 'Straff' ELSE '' END AS Typ, " +
            "replace(replace(replace(replace(replace(replace(replace(replace(replace(HL.`text`,'_6','6'),'_9','9'),'_mål','mål'),'_Utanf','Utanf'),'ur_','ur'),'UppstUtanför_','Uppst_Utanför'),'__','_'),'Uppst_Straff_Mål_','Uppst_Straff_Mål'),'_',' ') AS Text, " +
            "CASE WHEN HL.`text` LIKE '%Övertramp%' THEN 1 WHEN HL.`text` LIKE '%Offensiv_stuermer%' THEN 1 WHEN HL.`text` LIKE '%Stegfel%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Tappad_boll%' THEN 1 WHEN HL.`text` LIKE '%Dubbelstuds%' THEN 1 WHEN HL.`text` LIKE '%Övrigt_regelfel%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Felaktig_spärr%' THEN 1 WHEN HL.`text` LIKE '%TeknisktFel%' THEN 1 WHEN HL.`text` LIKE '%Passmiss%' THEN 1 ELSE 0 END AS TeknisktFel, " +
            "CASE WHEN S.position='MV' THEN 0 ELSE CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END END AS Avslut, " +
            "CASE WHEN S.position='MV' THEN 0 ELSE HL.is_goal END AS Mål " +
            "FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE H.game_id=@MatchID ORDER BY H.seconds DESC;";
        await using var cmd3 = new MySqlCommand(sql, connection);
        cmd3.Parameters.AddWithValue("@MatchID", händelse.MatchID);
        await using var reader = await cmd3.ExecuteReaderAsync();
        var list = new List<EventsTyp>();
        while (await reader.ReadAsync())
            list.Add(new EventsTyp
            {
                Tid = reader["Tid"].ToString() ?? "",
                Namn = reader["namn"].ToString() ?? "",
                Typ = reader["Typ"].ToString() ?? "",
                Händelse = reader["Text"].ToString() ?? "",
                TeknisktFel = reader["TeknisktFel"].ToString() ?? "",
                Zon = reader["Zon"].ToString() ?? "",
                Avslut = reader["Avslut"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<EventsTyp>?> GetHandelseUppdelad2(string matchId)
    {
        const string sql =
            "SELECT TIME_FORMAT(SEC_TO_TIME(H.seconds),'%i:%s') Tid, concat(S.shirt_number,' ',S.last_name) AS namn, H.phase_id Fas, Z.name Zon, " +
            "CASE WHEN HL.`text` LIKE 'Uppst%' THEN 'Uppst' WHEN HL.`text` LIKE 'Fas1%' THEN 'Fas1' WHEN HL.`text` LIKE 'Fas2%' THEN 'Fas2' " +
            "WHEN HL.`text` LIKE 'TeknisktFel%' THEN 'TeknisktFel' WHEN HL.`text` LIKE 'Straff%' THEN 'Straff' ELSE '' END AS Typ, " +
            "replace(replace(replace(replace(replace(replace(replace(replace(replace(HL.`text`,'_6','6'),'_9','9'),'_mål','mål'),'_Utanf','Utanf'),'ur_','ur'),'UppstUtanför_','Uppst_Utanför'),'__','_'),'Uppst_Straff_Mål_','Uppst_Straff_Mål'),'_',' ') AS Text, " +
            "CASE WHEN HL.`text` LIKE '%Övertramp%' THEN 1 WHEN HL.`text` LIKE '%Offensiv_stuermer%' THEN 1 WHEN HL.`text` LIKE '%Stegfel%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Tappad_boll%' THEN 1 WHEN HL.`text` LIKE '%TeknisktFel%' THEN 1 WHEN HL.`text` LIKE '%Dubbelstuds%' THEN 1 WHEN HL.`text` LIKE '%Övrigt_regelfel%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Passmiss%' THEN 1 ELSE 0 END AS TeknisktFel, " +
            "CASE WHEN S.position='MV' THEN 0 ELSE CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END END AS Avslut, " +
            "CASE WHEN S.position='MV' THEN 0 ELSE HL.is_goal END AS Mål " +
            "FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE H.game_id=@MatchID ORDER BY H.seconds DESC;";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EventsTyp>();
        while (await reader.ReadAsync())
            list.Add(new EventsTyp
            {
                Tid = reader["Tid"].ToString() ?? "",
                Namn = reader["namn"].ToString() ?? "",
                Typ = reader["Typ"].ToString() ?? "",
                Händelse = reader["Text"].ToString() ?? "",
                TeknisktFel = reader["TeknisktFel"].ToString() ?? "",
                Zon = reader["Zon"].ToString() ?? "",
                Avslut = reader["Avslut"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<EventsSam>?> GetHandelseSam(string matchId)
    {
        const string sql =
            "SELECT concat(S.shirt_number,' ',S.last_name) AS namn, " +
            "sum(CASE WHEN HL.`text` LIKE '%Övertramp%' THEN 1 WHEN HL.`text` LIKE '%Offensiv_stuermer%' THEN 1 WHEN HL.`text` LIKE '%Stegfel%' THEN 1 WHEN HL.`text` LIKE '%Tappad_boll%' THEN 1 WHEN HL.`text` LIKE '%Dubbelstuds%' THEN 1 WHEN HL.`text` LIKE '%Övrigt_regelfel%' THEN 1 WHEN HL.`text` LIKE '%TeknisktFel%' THEN 1 WHEN HL.`text` LIKE '%Passmiss%' THEN 1 ELSE 0 END) AS TeknisktFel, " +
            "sum(CASE WHEN S.position='MV' THEN 0 ELSE CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END END) AS Avslut, " +
            "sum(CASE WHEN S.position='MV' THEN 0 ELSE HL.is_goal END) AS Mål, " +
            "sum(CASE WHEN S.position='MV' THEN 0 ELSE HL.is_goal END) / " +
            "sum(CASE WHEN S.position='MV' THEN 0 ELSE CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END END) AS Målperavslut, " +
            "S.position Position FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE H.game_id=@MatchID GROUP BY concat(S.shirt_number,' ',S.last_name), S.position ORDER BY concat(S.shirt_number,' ',S.last_name);";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EventsSam>();
        while (await reader.ReadAsync())
            list.Add(new EventsSam
            {
                namn = reader["namn"].ToString() ?? "",
                TekniskaFel = reader["TeknisktFel"].ToString() ?? "",
                Avslut = reader["Avslut"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? "",
                Målperavslut = reader["Målperavslut"].ToString() ?? "",
                Position = reader["Position"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<EventsSam>?> GetHandelseSamEJ_MV(string matchId)
    {
        const string sql =
            "SELECT concat(S.shirt_number,' ',S.last_name) AS namn, " +
            "sum(CASE WHEN HL.`text` LIKE '%Övertramp%' THEN 1 WHEN HL.`text` LIKE '%Offensiv_stuermer%' THEN 1 WHEN HL.`text` LIKE '%TeknisktFel%' THEN 1 WHEN HL.`text` LIKE '%Stegfel%' THEN 1 WHEN HL.`text` LIKE '%Tappad_boll%' THEN 1 WHEN HL.`text` LIKE '%Dubbelstuds%' THEN 1 WHEN HL.`text` LIKE '%Övrigt_regelfel%' THEN 1 WHEN HL.`text` LIKE '%Passmiss%' THEN 1 ELSE 0 END) AS TeknisktFel, " +
            "sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END) AS Avslut, " +
            "sum(HL.is_goal) AS Mål, " +
            "sum(HL.is_goal) / " +
            "sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END) AS Målperavslut, " +
            "S.position Position FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE S.position<>'MV' AND H.game_id=@MatchID GROUP BY concat(S.shirt_number,' ',S.last_name), S.position ORDER BY concat(S.shirt_number,' ',S.last_name);";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EventsSam>();
        while (await reader.ReadAsync())
            list.Add(new EventsSam
            {
                namn = reader["namn"].ToString() ?? "",
                TekniskaFel = reader["TeknisktFel"].ToString() ?? "",
                Avslut = reader["Avslut"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? "",
                Målperavslut = reader["Målperavslut"].ToString() ?? "",
                Position = reader["Position"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<Malvakt>?> GetMalvakt(string matchId)
    {
        const string sql =
            "SELECT sum(CASE WHEN HL.`text` LIKE '%mål%' THEN CASE WHEN H.event_type_id=230 THEN 0 ELSE 1 END ELSE 0 END) Mål, " +
            "sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END) Raddningar, " +
            "sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END) / (sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 ELSE 0 END)+sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END)) procent " +
            "FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE H.game_id=@MatchID AND S.position='MV';";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Malvakt>();
        while (await reader.ReadAsync())
            list.Add(new Malvakt
            {
                Mål = reader["Mål"].ToString() ?? "",
                Raddningar = reader["Raddningar"].ToString() ?? "",
                Procent = reader["procent"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<Malvakt2>?> GetMalvakt2(string matchId)
    {
        const string sql =
            "SELECT concat(S.shirt_number,' ',S.last_name) AS namn, " +
            "sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 ELSE 0 END) Mål, " +
            "sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END) Raddningar, " +
            "sum(CASE WHEN HL.`text` LIKE '%fel%' THEN 1 ELSE 0 END) Fel, " +
            "sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END) / (sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 ELSE 0 END)+sum(CASE WHEN HL.`text` LIKE '%räddning%' THEN 1 ELSE 0 END)) procent " +
            "FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id " +
            "WHERE H.game_id=@MatchID AND S.position='MV' GROUP BY concat(S.shirt_number,' ',S.last_name);";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Malvakt2>();
        while (await reader.ReadAsync())
            list.Add(new Malvakt2
            {
                Namn = reader["namn"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? "",
                Raddningar = reader["Raddningar"].ToString() ?? "",
                Procent = reader["procent"].ToString() ?? "",
                Fel = reader["Fel"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<EventType>> GetEventTypes()
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, `text`, is_goal FROM event_type;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EventType>();
        while (await reader.ReadAsync())
            list.Add(new EventType
            {
                Id = reader["id"].ToString() ?? "",
                Text = reader["text"].ToString() ?? "",
                IsGoal = reader["is_goal"] != DBNull.Value && Convert.ToInt32(reader["is_goal"]) == 1
            });
        return list;
    }

    public async Task<List<HA>?> GetHA(string hid)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `text` AS Händelse, id AS HändelseID FROM event_type WHERE id>@HID;", connection);
        cmd.Parameters.AddWithValue("@HID", hid);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<HA>();
        while (await reader.ReadAsync())
            list.Add(new HA
            {
                HändelseID = reader["HändelseID"].ToString() ?? "",
                Händelse = reader["Händelse"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<antH>?> GetAntH()
    {
        // AntalHändelser fanns inte som tabell i gamla DB; beräknas nu från schemat.
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT U.email AS UserName, COUNT(E.id) AS antalHändelser " +
            "FROM app_user U JOIN team T ON T.user_id=U.id JOIN game G ON G.team_id=T.id " +
            "JOIN game_event E ON E.game_id=G.id GROUP BY U.email ORDER BY antalHändelser DESC;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<antH>();
        while (await reader.ReadAsync())
            list.Add(new antH { Namn = reader["UserName"].ToString() ?? "", antHa = reader["antalHändelser"].ToString() ?? "" });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<antM>?> GetAntM()
    {
        // AntalMatcher fanns inte som tabell i gamla DB; beräknas nu från schemat.
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT U.email AS UserName, COUNT(DISTINCT G.id) AS antalMatcher " +
            "FROM app_user U JOIN team T ON T.user_id=U.id JOIN game G ON G.team_id=T.id " +
            "GROUP BY U.email ORDER BY antalMatcher DESC;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<antM>();
        while (await reader.ReadAsync())
            list.Add(new antM { Namn = reader["UserName"].ToString() ?? "", antMa = reader["antalMatcher"].ToString() ?? "" });
        return list.Count > 0 ? list : null;
    }

    public async Task<List<EventsSam>?> GetHandelseTot(string matchId)
    {
        const string sql =
            "SELECT sum(CASE WHEN HL.`text` LIKE '%Övertramp%' THEN 1 WHEN HL.`text` LIKE '%Offensiv_stuermer%' THEN 1 WHEN HL.`text` LIKE '%Stegfel%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Tappad_boll%' THEN 1 WHEN HL.`text` LIKE '%Dubbelstuds%' THEN 1 WHEN HL.`text` LIKE '%Övrigt_regelfel%' THEN 1 WHEN HL.`text` LIKE '%Passmiss%' THEN 1 ELSE 0 END) AS TeknisktFel, " +
            "sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END) AS Avslut, " +
            "sum(HL.is_goal) AS Mål, " +
            "sum(HL.is_goal) / " +
            "sum(CASE WHEN HL.`text` LIKE '%mål%' THEN 1 WHEN HL.`text` LIKE '%_6m%' THEN 1 WHEN HL.`text` LIKE '%_9m%' THEN 1 WHEN HL.`text` LIKE '%Genombrott%' THEN 1 " +
            "WHEN HL.`text` LIKE '%Räddning%' THEN 1 WHEN HL.`text` LIKE '%Utanför%' THEN 1 WHEN HL.`text` LIKE '%Skott_i_täcket%' THEN 1 ELSE 0 END) AS Målperavslut " +
            "FROM event_type HL JOIN game_event H ON H.event_type_id=HL.id " +
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id WHERE H.game_id=@MatchID;";
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EventsSam>();
        while (await reader.ReadAsync())
            list.Add(new EventsSam
            {
                TekniskaFel = reader["TeknisktFel"].ToString() ?? "",
                Avslut = reader["Avslut"].ToString() ?? "",
                Mål = reader["Mål"].ToString() ?? "",
                Målperavslut = reader["Målperavslut"].ToString() ?? ""
            });
        return list.Count > 0 ? list : null;
    }

    public async Task<bool> DelDemo()
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM game_event WHERE game_id=43 AND seconds>500;", connection);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task AddHandelseLista(string handelse)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        // event_type.id är inte auto_increment (fasta koder) => beräkna nästa id.
        await using var cmd = new MySqlCommand(
            "INSERT INTO event_type (id, `text`, is_goal) SELECT COALESCE(MAX(id),0)+1, @Händelse, 0 FROM event_type;", connection);
        cmd.Parameters.AddWithValue("@Händelse", handelse);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- Mail ----

    public bool SkickaMail(string mottagare, string ämne, string text)
    {
        try
        {
            var mail = new MailMessage();
            mail.From = new MailAddress(_config["Mail:From"] ?? "mm@matchmate.se");
            mail.Subject = ämne;
            mail.Body = text;
            mail.To.Add(mottagare);
            var smtp = new SmtpClient
            {
                Host = _config["Mail:SmtpHost"] ?? "websmtp.simply.com",
                Port = int.TryParse(_config["Mail:SmtpPort"], out var p) ? p : 587,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(
                    _config["Mail:From"] ?? "",
                    _config["Mail:Password"] ?? "")
            };
            smtp.Send(mail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kunde inte skicka e-post till {Mottagare}", mottagare);
            return false;
        }
    }
}
