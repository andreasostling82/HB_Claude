namespace HB_Claude.Models;

public enum Fas
{
    ej_angivet = 0,
    Uppställt = 1,
    Fas1 = 2,
    Fas2 = 3
}

public enum Zon
{
    ej_angivet = 0,
    V1 = 1,
    V2 = 2,
    O3 = 3,
    H2 = 4,
    H1 = 5
}

public class User
{
    public string UserID { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string typ { get; set; } = "";
    public string status { get; set; } = "";
}

public class Lag
{
    public string Namn { get; set; } = "";
    public string HemmaHall { get; set; } = "";
    public string Serie { get; set; } = "";
    public string KM { get; set; } = "";
    public string UserID { get; set; } = "";
    public string LagID { get; set; } = "";
}

public class Spelare
{
    public string SpelareID { get; set; } = "";
    public string Efternamn { get; set; } = "";
    public string Förnamn { get; set; } = "";
    public string LagID { get; set; } = "";
    public string Position { get; set; } = "";
    public string Nummer { get; set; } = "";
    public string XNummer { get; set; } = "";
}

public class HBSpelare
{
    public string SpID { get; set; } = "";
    public string Namn { get; set; } = "";
    public string Fnamn { get; set; } = "";
    public string Enamn { get; set; } = "";
    public string Nummer { get; set; } = "";
    public string XNummer { get; set; } = "";
    public string position { get; set; } = "";
}

public class Match
{
    public string LagID { get; set; } = "";
    public string MatchID { get; set; } = "";
    public string Datum { get; set; } = "";
    public string Motståndare { get; set; } = "";
    public string Plats { get; set; } = "";
    public string Status { get; set; } = "";
    public string Titel { get; set; } = "";
}

public class Matcher
{
    public string ID { get; set; } = "";
    public string Datum { get; set; } = "";
    public string Motståndare { get; set; } = "";
    public string titel { get; set; } = "";
    public string status { get; set; } = "";
}

public class Händelse
{
    public string Händelsen { get; set; } = "";
    public string HändelseID { get; set; } = "";
    public string MatchID { get; set; } = "";
    public string SpelareID { get; set; } = "";
    public string Fas { get; set; } = "0";
    public string Zon { get; set; } = "0";
    public string Tids { get; set; } = "0";
}

public class EventsTyp
{
    public string Tid { get; set; } = "";
    public string Namn { get; set; } = "";
    public string Typ { get; set; } = "";
    public string Händelse { get; set; } = "";
    public string TeknisktFel { get; set; } = "";
    public string Zon { get; set; } = "";
    public string Mål { get; set; } = "";
    public string Avslut { get; set; } = "";
}

public class EventsSam
{
    public string namn { get; set; } = "";
    public string TekniskaFel { get; set; } = "";
    public string Avslut { get; set; } = "";
    public string Mål { get; set; } = "";
    public string Målperavslut { get; set; } = "";
    public string Position { get; set; } = "";
}

public class Malvakt
{
    public string Raddningar { get; set; } = "";
    public string Procent { get; set; } = "";
    public string Mål { get; set; } = "";
}

public class Malvakt2
{
    public string Namn { get; set; } = "";
    public string Raddningar { get; set; } = "";
    public string Procent { get; set; } = "";
    public string Mål { get; set; } = "";
    public string Fel { get; set; } = "";
}

public class antHandelser
{
    public int ant { get; set; }
    public string namn { get; set; } = "";
    public string handelse { get; set; } = "";
}

public class HA
{
    public string HändelseID { get; set; } = "";
    public string Händelse { get; set; } = "";
}

public class antH
{
    public string Namn { get; set; } = "";
    public string antHa { get; set; } = "";
}

public class antM
{
    public string Namn { get; set; } = "";
    public string antMa { get; set; } = "";
}

// ---- Offline / sync ----

public class EventType
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsGoal { get; set; }
}

// En köad operation från offline-klienten.
// Kind: "event" (registrerad händelse), "status" (byt matchstatus), "startmarker" (start-rad).
public class SyncOp
{
    public string Kind { get; set; } = "event";
    public string ClientId { get; set; } = "";
    public string MatchId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Handelsen { get; set; } = "";
    public string Fas { get; set; } = "0";
    public string Zon { get; set; } = "0";
    public string Tids { get; set; } = "0";
    public string Status { get; set; } = "";
}

public class SyncBatch
{
    public List<SyncOp> Ops { get; set; } = new();
}

public class SyncResult
{
    public List<string> Confirmed { get; set; } = new();
    public List<string> Failed { get; set; } = new();
}
