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

    public async Task<User?> GetUserFromHash(string hash, string email)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        // Matcha på BÅDE e-post och hash. Annars kan två konton med samma lösenord
        // (= samma hash) logga in som varandra, eftersom hashen ensam inte är unik.
        await using var cmd = new MySqlCommand(
            "SELECT IFNULL(UU.delegate_user_id, U.id) AS UserID, U.email AS UserName, U.password_hash AS Password, " +
            "U.role AS typ, U.status AS status " +
            "FROM app_user U LEFT JOIN user_delegate UU ON U.id=UU.user_id " +
            "WHERE U.password_hash=@hsh AND replace(U.email,'+','')=@email;", connection);
        cmd.Parameters.AddWithValue("@hsh", hash);
        cmd.Parameters.AddWithValue("@email", email);
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

    // Verifierar ett lösenord mot samma trestegs-fallback som inloggningen använder
    // (rått lösenord, SHA256(email+lösenord), MD5(lösenord+salt)). Returnerar användaren
    // om något schema matchar, annars null.
    public async Task<User?> AuthenticateUser(string email, string password)
    {
        var us = await GetUser(email, password);
        if (us == null)
            us = await GetUserFromHash(Hash256(email, password), email);
        if (us == null)
            us = await GetUserFromHash((await GetHash(password)).Trim('"'), email);
        return us;
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

    // ---- Testkonto (login utan lösenord + demodata) ----

    // E-post för det publika testkontot. Lösenordshashen sätts till ett värde som
    // aldrig kan matcha ett riktigt lösenord, så kontot bara nås via testknappen.
    public const string TestEmail = "testkonto@matchmate.se";

    // Hämtar testkontot, skapar det om det saknas. Returnerar id + roll.
    public async Task<User> EnsureTestAccount()
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using (var find = new MySqlCommand(
            "SELECT id, role FROM app_user WHERE email=@e LIMIT 1;", connection))
        {
            find.Parameters.AddWithValue("@e", TestEmail);
            await using var r = await find.ExecuteReaderAsync();
            if (await r.ReadAsync())
                return new User { UserID = r["id"].ToString() ?? "", typ = r["role"].ToString() ?? "1", UserName = TestEmail };
        }
        await using (var ins = new MySqlCommand(
            "INSERT INTO app_user (email, password_hash, role, status) VALUES (@e, @p, '1', 'aktiv');", connection))
        {
            ins.Parameters.AddWithValue("@e", TestEmail);
            ins.Parameters.AddWithValue("@p", "TESTKONTO_INGEN_INLOGGNING_" + Guid.NewGuid().ToString("N"));
            await ins.ExecuteNonQueryAsync();
            return new User { UserID = ins.LastInsertedId.ToString(), typ = "1", UserName = TestEmail };
        }
    }

    // Seedar testlag, testspelare, testmatcher och några matchhändelser om kontot
    // saknar lag. Självläkande: körs varje gång testknappen används men gör inget
    // om data redan finns, så testarnas ändringar bevaras tills laget töms.
    public async Task SeedTestDataIfEmpty(string userId)
    {
        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync();

        await using (var chk = new MySqlCommand("SELECT COUNT(*) FROM team WHERE user_id=@u;", conn))
        {
            chk.Parameters.AddWithValue("@u", userId);
            if (Convert.ToInt64(await chk.ExecuteScalarAsync()) > 0) return;
        }

        async Task<long> Exec(string sql, params (string, object)[] ps)
        {
            await using var c = new MySqlCommand(sql, conn);
            foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
            await c.ExecuteNonQueryAsync();
            return c.LastInsertedId;
        }

        Task<long> AddTeam(string namn, string hall, string serie, string kat) =>
            Exec("INSERT INTO team (name, home_venue, series, user_id, category) VALUES (@n,@h,@s,@u,@k);",
                ("@n", namn), ("@h", hall), ("@s", serie), ("@u", userId), ("@k", kat));

        Task<long> AddPlayer(long teamId, string fn, string en, int nr, string pos) =>
            Exec("INSERT INTO player (team_id, first_name, last_name, position, shirt_number) VALUES (@t,@f,@e,@p,@n);",
                ("@t", teamId), ("@f", fn), ("@e", en), ("@p", pos), ("@n", nr));

        Task<long> AddGame(long teamId, string datum, string mots, string plats, string status) =>
            Exec("INSERT INTO game (played_on, team_id, opponent, venue, status) VALUES (@d,@t,@o,@v,@s);",
                ("@d", datum), ("@t", teamId), ("@o", mots), ("@v", plats), ("@s", status));

        Task<long> AddEvent(long gameId, long playerId, int typ, int zon, int fas, int sek) =>
            Exec("INSERT INTO game_event (game_id, player_id, event_type_id, phase_id, zone_id, seconds) VALUES (@g,@p,@t,@f,@z,@s);",
                ("@g", gameId), ("@p", playerId), ("@t", typ), ("@f", fas), ("@z", zon), ("@s", sek));

        int[] goalTypes = { 15, 20, 36 }; // _Mål_, _9m, _6m (is_goal=1)
        const int miss = 14;              // _Utanför
        const int save = 16;              // Räddning_

        // Fyller en avslutad match med avslut (mål + missar) för utespelarna och
        // några räddningar för målvakten (index 0). pl[0] = MV.
        async Task SeedEvents(long g, List<long> pl)
        {
            int sek = 60;
            for (int i = 1; i < pl.Count; i++)
            {
                await AddEvent(g, pl[i], goalTypes[i % goalTypes.Length], 1 + (i % 5), 1 + (i % 3), sek); sek += 45;
                await AddEvent(g, pl[i], miss, 1 + ((i + 2) % 5), 1 + ((i + 1) % 3), sek); sek += 40;
            }
            await AddEvent(g, pl[0], save, 1, 1, sek); sek += 30;
            await AddEvent(g, pl[0], save, 2, 2, sek);
        }

        // Lag 1 – Herr
        var t1 = await AddTeam("Testlaget Herr", "Testhallen", "Division 3", "M");
        var p1 = new List<long>
        {
            await AddPlayer(t1, "Anders", "Målberg", 1, "MV"),
            await AddPlayer(t1, "Erik", "Vänsterström", 7, "V9"),
            await AddPlayer(t1, "Johan", "Högberg", 9, "H9"),
            await AddPlayer(t1, "Karl", "Mittfelt", 10, "M9"),
            await AddPlayer(t1, "Sven", "Linjeman", 4, "M6"),
            await AddPlayer(t1, "Olof", "Kantberg", 11, "V6"),
            await AddPlayer(t1, "Nils", "Backman", 5, "H6"),
            await AddPlayer(t1, "Per", "Skytt", 8, "M9"),
        };

        // Lag 2 – Dam
        var t2 = await AddTeam("Testlaget Dam", "Testhallen", "Division 2", "W");
        var p2 = new List<long>
        {
            await AddPlayer(t2, "Sofia", "Målqvist", 1, "MV"),
            await AddPlayer(t2, "Emma", "Vänsterlund", 7, "V9"),
            await AddPlayer(t2, "Klara", "Högström", 9, "H9"),
            await AddPlayer(t2, "Maja", "Mittberg", 10, "M9"),
            await AddPlayer(t2, "Alva", "Linjqvist", 4, "M6"),
            await AddPlayer(t2, "Wilma", "Kantell", 11, "V6"),
        };

        // Matcher – lag 1 (två avslutade med händelser, en planerad)
        var g1 = await AddGame(t1, "2026-04-20", "HK Rival", "Testhallen", "Avslutad");
        var g2 = await AddGame(t1, "2026-05-10", "IK Motstånd", "Bortahallen", "Avslutad");
        await AddGame(t1, "2026-05-24", "BK Framtid", "Testhallen", "Planerad");
        await SeedEvents(g1, p1);
        await SeedEvents(g2, p1);

        // Matcher – lag 2 (en avslutad med händelser, en planerad)
        var g3 = await AddGame(t2, "2026-04-28", "HF Syd", "Bortahallen", "Avslutad");
        await AddGame(t2, "2026-05-12", "Dam IF", "Testhallen", "Planerad");
        await SeedEvents(g3, p2);
    }

    // ---- Admin / kontoöversikt ----

    // Översikt över alla konton med antal lag, matcher och spelade (avslutade)
    // matcher per konto. Används av admin-sidan för att följa aktiva konton.
    public async Task<List<KontoRad>> GetKontoOversikt()
    {
        var list = new List<KontoRad>();
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT u.email, u.status, " +
            "(SELECT COUNT(*) FROM team t WHERE t.user_id=u.id) AS antal_lag, " +
            "(SELECT COUNT(*) FROM game g JOIN team t ON t.id=g.team_id WHERE t.user_id=u.id) AS antal_matcher, " +
            "(SELECT COUNT(*) FROM game g JOIN team t ON t.id=g.team_id WHERE t.user_id=u.id AND g.status='Avslutad') AS spelade " +
            "FROM app_user u ORDER BY antal_matcher DESC, antal_lag DESC, u.email;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new KontoRad
            {
                Epost = reader["email"].ToString() ?? "",
                Status = reader["status"].ToString() ?? "",
                AntalLag = Convert.ToInt32(reader["antal_lag"]),
                AntalMatcher = Convert.ToInt32(reader["antal_matcher"]),
                Spelade = Convert.ToInt32(reader["spelade"])
            });
        return list;
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
                "FROM player WHERE team_id=@LagID AND position<>'SYS' " +
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
                "alt_shirt_number AS XNummer, shirt_number AS Nummer FROM player WHERE team_id=@LagID AND position<>'SYS';", connection);
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
        cmd.Parameters.AddWithValue("@Nummer", int.TryParse(spelare.Nummer, out var nr) ? nr : 0);
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
        cmd.Parameters.AddWithValue("@Nummer", int.TryParse(spelare.Nummer, out var nr) ? nr : 0);
        cmd.Parameters.AddWithValue("@Position", spelare.Position);
        cmd.Parameters.AddWithValue("@Alt", int.TryParse(spelare.XNummer, out var alt) ? alt : (object)DBNull.Value);
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

    // Spelstopp är en lag-/matchhändelse utan spelare. game_event.player_id är dock
    // NOT NULL med FK, så vi ankrar händelsen på en dold "systemspelare" (position 'SYS')
    // som skapas per lag vid behov. Systemspelaren filtreras bort ur alla spelarlistor
    // och statistik-queries (WHERE position<>'SYS'), så den syns aldrig i appen.
    private async Task<string> EnsureSystemPlayer(MySqlConnection connection, string teamId)
    {
        await using (var find = new MySqlCommand(
            "SELECT id FROM player WHERE team_id=@t AND position='SYS' LIMIT 1;", connection))
        {
            find.Parameters.AddWithValue("@t", teamId);
            var existing = (await find.ExecuteScalarAsync())?.ToString();
            if (!string.IsNullOrEmpty(existing)) return existing;
        }

        await using (var ins = new MySqlCommand(
            "INSERT INTO player (team_id, first_name, last_name, position, shirt_number) " +
            "VALUES (@t, 'System', 'Spelstopp', 'SYS', 0);", connection))
        {
            ins.Parameters.AddWithValue("@t", teamId);
            await ins.ExecuteNonQueryAsync();
            return ins.LastInsertedId.ToString();
        }
    }

    private async Task<string> EnsureSpelstoppEventType(MySqlConnection connection)
    {
        await using (var find = new MySqlCommand(
            "SELECT id FROM event_type WHERE `text`='Spelstopp' LIMIT 1;", connection))
        {
            var existing = (await find.ExecuteScalarAsync())?.ToString();
            if (!string.IsNullOrEmpty(existing)) return existing;
        }
        // event_type.id är inte auto_increment (fasta koder) => beräkna nästa id.
        await using (var ins = new MySqlCommand(
            "INSERT INTO event_type (id, `text`, is_goal) " +
            "SELECT COALESCE(MAX(id),0)+1, 'Spelstopp', 0 FROM event_type " +
            "WHERE NOT EXISTS (SELECT 1 FROM event_type WHERE `text`='Spelstopp');", connection))
        {
            await ins.ExecuteNonQueryAsync();
        }
        await using (var re = new MySqlCommand(
            "SELECT id FROM event_type WHERE `text`='Spelstopp' LIMIT 1;", connection))
        {
            return (await re.ExecuteScalarAsync())?.ToString() ?? "0";
        }
    }

    public async Task<bool> AddSpelstopp(string matchId, string tids, string? clientEventId = null)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();

        // Härled lag från matchen och säkerställ systemspelare + händelsetyp.
        string teamId;
        await using (var t = new MySqlCommand("SELECT team_id FROM game WHERE id=@g LIMIT 1;", connection))
        {
            t.Parameters.AddWithValue("@g", matchId);
            teamId = (await t.ExecuteScalarAsync())?.ToString() ?? "";
        }
        if (string.IsNullOrEmpty(teamId)) return false;

        var playerId = await EnsureSystemPlayer(connection, teamId);
        var hanID = await EnsureSpelstoppEventType(connection);

        // client_event_id (offline-synk) gör inserten idempotent – se AddHändelse.
        await using var cmd = new MySqlCommand(
            "INSERT INTO game_event (seconds, game_id, player_id, event_type_id, phase_id, zone_id, client_event_id) " +
            "VALUES (@Tids, @MatchID, @SpelareID, @HändelseID, 0, 0, @Cid) " +
            "ON DUPLICATE KEY UPDATE client_event_id = client_event_id;", connection);
        cmd.Parameters.AddWithValue("@Tids", tids);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        cmd.Parameters.AddWithValue("@SpelareID", playerId);
        cmd.Parameters.AddWithValue("@HändelseID", hanID);
        cmd.Parameters.AddWithValue("@Cid", string.IsNullOrEmpty(clientEventId) ? DBNull.Value : clientEventId);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<int> GetSpelstoppCount(string matchId)
    {
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM game_event H JOIN event_type HL ON HL.id=H.event_type_id " +
            "WHERE H.game_id=@MatchID AND HL.`text`='Spelstopp';", connection);
        cmd.Parameters.AddWithValue("@MatchID", matchId);
        var r = await cmd.ExecuteScalarAsync();
        return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
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

        // Självläkande: om händelsenamnet saknas i event_type (t.ex. en målvakts-
        // placering som inte förseedats) skapas raden i stället för att lagras som
        // "Okänt" (id=0). is_goal=0 – målvaktsmål räknas via text LIKE '%mål%', inte
        // is_goal, och fältmål har redan sina rader.
        if (hanID == "0" && !string.IsNullOrWhiteSpace(händelse.Händelsen))
        {
            await using (var heal = new MySqlCommand(
                "INSERT INTO event_type (id, `text`, is_goal) " +
                "SELECT COALESCE(MAX(id),0)+1, @nm, 0 FROM event_type " +
                "WHERE NOT EXISTS (SELECT 1 FROM event_type WHERE `text`=@nm);", connection))
            {
                heal.Parameters.AddWithValue("@nm", händelse.Händelsen);
                await heal.ExecuteNonQueryAsync();
            }
            await using (var re = new MySqlCommand(
                "SELECT id FROM event_type WHERE `text`=@nm LIMIT 1;", connection))
            {
                re.Parameters.AddWithValue("@nm", händelse.Händelsen);
                hanID = (await re.ExecuteScalarAsync())?.ToString() ?? "0";
            }
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
            "CASE WHEN S.position='SYS' THEN '' ELSE concat(S.shirt_number,' ',S.last_name) END AS namn, H.phase_id Fas, Z.name Zon," +
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
            "SELECT TIME_FORMAT(SEC_TO_TIME(H.seconds),'%i:%s') Tid, CASE WHEN S.position='SYS' THEN '' ELSE concat(S.shirt_number,' ',S.last_name) END AS namn, H.phase_id Fas, Z.name Zon," +
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
            "WHERE H.game_id=@MatchID AND S.position<>'SYS' GROUP BY concat(S.shirt_number,' ',S.last_name), S.position ORDER BY concat(S.shirt_number,' ',S.last_name);";
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
            "WHERE S.position NOT IN ('MV','SYS') AND H.game_id=@MatchID GROUP BY concat(S.shirt_number,' ',S.last_name), S.position ORDER BY concat(S.shirt_number,' ',S.last_name);";
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
            "JOIN player S ON S.id=H.player_id JOIN zone Z ON Z.id=H.zone_id WHERE H.game_id=@MatchID AND S.position<>'SYS';";
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
