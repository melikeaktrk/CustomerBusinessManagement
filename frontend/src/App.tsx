import { useEffect, useState } from "react";
import {
  BrowserRouter,
  Link,
  Navigate,
  Route,
  Routes,
  useNavigate,
} from "react-router-dom";
import { api } from "./api";
import "./index.css";
type Row = Record<string, any>;
const moduleNames: Record<string, string> = {
  customers: "Müşteri",
  employees: "Personel",
  accounting: "Muhasebe Kaydı",
  cash: "Kasa Hareketi",
  zreports: "Z Raporu",
};
function capitalizeWords(value: string) {
  return value
    .trim()
    .toLocaleLowerCase("tr-TR")
    .split(/\s+/)
    .map((word) => word.charAt(0).toLocaleUpperCase("tr-TR") + word.slice(1))
    .join(" ");
}
function formatDate(value: unknown, includeTime = false) {
  if (typeof value !== "string" && !(value instanceof Date))
    return String(value ?? "—");
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  const options: Intl.DateTimeFormatOptions = includeTime
    ? {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
        hourCycle: "h23",
      }
    : { day: "2-digit", month: "2-digit", year: "numeric" };
  return new Intl.DateTimeFormat("tr-TR", options).format(date);
}
function displayValue(key: string, value: unknown, module = "accounting") {
  if (
    (key === "firstName" || key === "lastName") &&
    typeof value === "string"
  ) {
    return capitalizeWords(value);
  }
  if (
    (key === "transactionType" || key === "TransactionType") &&
    typeof value === "number"
  ) {
    if (module === "cash") return value === 0 ? "Kasa girişi" : "Kasa çıkışı";
    return value === 0 ? "Gelir" : "Gider";
  }
  if (
    (key === "transactionType" || key === "TransactionType") &&
    typeof value === "string"
  ) {
    const numeric = Number(value);
    if (value.trim() !== "" && Number.isInteger(numeric)) {
      if (module === "cash")
        return numeric === 0 ? "Kasa girişi" : "Kasa çıkışı";
      return numeric === 0 ? "Gelir" : "Gider";
    }
  }
  if (/date|createdAt|updatedAt/i.test(key) && typeof value === "string") {
    return formatDate(
      value,
      /transactionDate|createdAt|updatedAt/i.test(key) && value.includes("T"),
    );
  }
  return degeriGoster(value);
}
function enumValue(value: unknown, module: "accounting" | "cash") {
  if (typeof value === "number") {
    return module === "cash"
      ? value === 0
        ? "CashIn"
        : "CashOut"
      : value === 0
        ? "Income"
        : "Expense";
  }
  if (typeof value === "string" && /^\d+$/.test(value)) {
    return enumValue(Number(value), module);
  }
  return value;
}
const links = [
  ["/", "⌂", "Gösterge Paneli"],
  ["/customers", "◉", "Müşteriler"],
  ["/employees", "♙", "Personel"],
  ["/accounting", "↗", "Muhasebe"],
  ["/cash", "◈", "Kasa"],
  ["/zreports", "▤", "Z Raporları"],
  ["/profile", "◎", "Profil"],
];
function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route
          path="*"
          element={
            <Guard>
              <Shell />
            </Guard>
          }
        />
      </Routes>
    </BrowserRouter>
  );
}
function Guard({ children }: { children: React.ReactNode }) {
  return sessionStorage.getItem("token") ? (
    children
  ) : (
    <Navigate to="/login" replace />
  );
}
function Login() {
  const nav = useNavigate();
  const [error, setError] = useState("");
  async function submit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    try {
      const { data } = await api.post("/Auth/login", {
        email: d.get("email"),
        password: d.get("password"),
      });
      // Oturum belirtecini sekme oturumu süresince sakla.
      sessionStorage.setItem("token", data.token);
      nav("/");
    } catch {
      setError(
        "E-posta veya şifre hatalı. Bilgilerinizi kontrol edip yeniden deneyin.",
      );
    }
  }
  return (
    <main className="login">
      <div className="login-brand">
        <div className="brand-icon">N</div>
        <span>Müşteri Yönetimi</span>
      </div>
      <section className="login-box">
        <small>YENİDEN HOŞ GELDİNİZ</small>
        <h1>Yönetim panelinize giriş yapın</h1>
        <p>Müşteri, personel ve finans işlemlerinizi tek yerden yönetin.</p>
        <form onSubmit={submit}>
          <label>
            E-posta adresi
            <input
              name="email"
              type="email"
              required
              placeholder="admin@ornek.com"
            />
          </label>
          <label>
            Şifre
            <input
              name="password"
              type="password"
              required
              placeholder="Şifrenizi girin"
            />
          </label>
          {error && <p className="error">{error}</p>}
          <button className="primary">
            Giriş yap <span>→</span>
          </button>
        </form>
        <div className="login-foot">
          Güvenli yönetici girişi · Müşteri İşletme Yönetimi
        </div>
      </section>
    </main>
  );
}
function Shell() {
  const path = location.pathname;
  const segment = path.split("/")[1];
  const moduleName = moduleNames[segment] ?? "Kayıt";
  const isCreate = path.endsWith("/new");
  const isEdit = path.endsWith("/edit");
  const isForm = isCreate || isEdit;
  const detail =
    path.split("/").length === 3 && Number.isFinite(Number(path.split("/")[2]));
  const listTitle = links.find((x) => x[0] === path)?.[2];
  const title = isCreate
    ? segment === "zreports"
      ? "Yeni Z Raporu Yükle"
      : `Yeni ${moduleName} Ekle`
    : isEdit
      ? segment === "accounting"
        ? "Muhasebe Kaydını Düzenle"
        : `${moduleName} Bilgilerini Düzenle`
      : detail
        ? segment === "accounting"
          ? "Muhasebe Kayıt Detayları"
          : `${moduleName} Detayları`
        : path.startsWith("/zreports/upload")
          ? "Z Raporları"
          : (listTitle ?? "Müşteri Yönetimi");
  const subtitle =
    path === "/"
      ? "İşletmenizde bugün olanlara genel bakış."
      : isForm
        ? isEdit
          ? "Kayıt bilgilerini güncelleyin."
          : "Yeni kayıt bilgilerini girin."
        : detail
          ? "Bu kaydın ayrıntılı bilgileri."
          : path.startsWith("/zreports/upload")
            ? "Rapor dosyasını yükleyip bilgileri inceleyin."
            : "Kayıtları görüntüleyin ve yönetin.";
  const nav = useNavigate();
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Link className="brand" to="/">
          <div className="brand-icon">N</div>
          <span>Müşteri Yönetimi</span>
        </Link>
        <div className="workspace">
          <div className="workspace-dot">A</div>
          <div>
            <strong>Örnek Şirket</strong>
            <small>Çalışma alanı</small>
          </div>
          <span className="chevron">⌄</span>
        </div>
        <small className="nav-label">ÇALIŞMA ALANI</small>
        <nav>
          {links.map(([to, icon, label]) => (
            <Link key={to} to={to} className={path === to ? "active" : ""}>
              <span className="nav-icon">{icon}</span>
              {label}
            </Link>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="support">
            {" "}
            <span>✦</span>
            <div>
              <b>Yardıma mı ihtiyacınız var?</b>
              <small>Yardım merkezini ziyaret edin</small>
            </div>
            ↗
          </div>
          <div className="account">
            <div className="avatar">AD</div>
            <div>
              <b>Yönetici</b>
              <small>Yönetici hesabı</small>
            </div>
            <button
              onClick={() => {
                sessionStorage.removeItem("token");
                nav("/login");
              }}
              title="Çıkış yap"
            >
              ↪
            </button>
          </div>
        </div>
      </aside>
      <main className="main">
        <header>
          <div className="crumb">
            Çalışma alanı <span>/</span> <b>{title}</b>
          </div>
          <div className="top-actions">
            <div className="search-mini">
              ⌕ <input placeholder="Her yerde ara..." />
              <kbd>⌘ K</kbd>
            </div>
            <button className="icon-button">♧</button>
            <div className="avatar">AD</div>
          </div>
        </header>
        <section className="content">
          <div className="page-heading">
            <div>
              <div className="eyebrow">
                {new Intl.DateTimeFormat("tr-TR", {
                  weekday: "long",
                  day: "numeric",
                  month: "long",
                  year: "numeric",
                })
                  .format(new Date())
                  .toLocaleUpperCase("tr-TR")}
              </div>
              <h1>
                {title === "Gösterge Paneli" ? "Günaydın, Yönetici" : title}
                <span className="wave">
                  {title === "Gösterge Paneli" ? " ✦" : ""}
                </span>
              </h1>
              <p>{subtitle}</p>
            </div>
            {path !== "/" &&
              path !== "/profile" &&
              !isForm &&
              !detail &&
              [
                "/customers",
                "/employees",
                "/accounting",
                "/cash",
                "/zreports",
              ].includes(path) &&
              !path.includes("/upload") && (
                <button
                  className="primary"
                  onClick={() =>
                    nav(
                      path === "/zreports" ? "/zreports/upload" : path + "/new",
                    )
                  }
                >
                  {path === "/zreports"
                    ? "+ Yeni Z Raporu Yükle"
                    : "＋ Yeni kayıt"}
                </button>
              )}
          </div>
          {path === "/" ? (
            <Dashboard />
          ) : path === "/profile" ? (
            <Profile />
          ) : path.includes("/new") || path.includes("/edit") ? (
            <FormPage />
          ) : path.includes("/zreports/upload") ? (
            <DataPage key="/zreports" />
          ) : detail ? (
            <DetailPage title={title} />
          ) : (
            <DataPage key={path} />
          )}
        </section>
        {path.startsWith("/zreports/upload") && <Upload />}
        <footer>
          © 2026 Müşteri İşletme Yönetimi{" "}
          <span>
            Sistem çalışıyor <i /> · Gizlilik · Destek
          </span>
        </footer>
      </main>
    </div>
  );
}
function Dashboard() {
  const [d, setD] = useState<Row | null>(null);
  const [dashboardLoading, setDashboardLoading] = useState(true);
  const [dashboardError, setDashboardError] = useState("");
  const [dashboardRefresh, setDashboardRefresh] = useState(0);
  const [selectedTransaction, setSelectedTransaction] = useState<Row | null>(
    null,
  );
  useEffect(() => {
    let isCurrentRequest = true;
    api
      .get("/Dashboard/summary")
      .then((r) => {
        if (isCurrentRequest) setD(r.data);
      })
      .catch(() => {
        if (!isCurrentRequest) return;
        setD(null);
        setDashboardError("Gösterge paneli verileri alınamadı.");
      })
      .finally(() => {
        if (isCurrentRequest) setDashboardLoading(false);
      });
    return () => {
      isCurrentRequest = false;
    };
  }, [dashboardRefresh]);
  const accounting = (d?.recentAccountingTransactions ?? []) as Row[];
  const cashTransactions = (d?.recentCashTransactions ?? []) as Row[];
  const reports = (d?.recentZReports ?? []) as Row[];
  const timeline: Row[] = [
    ...accounting
      .filter((row) => !row.isCancelled)
      .map((row) => ({ ...row, kind: "accounting" })),
    ...cashTransactions
      .filter((row) => !row.isCancelled)
      .map((row) => ({ ...row, kind: "cash" })),
    ...reports
      .filter((row) => row.isConfirmed)
      .map((row) => ({ ...row, kind: "report" })),
  ].sort(
    (a: Row, b: Row) =>
      new Date(b.transactionDate ?? b.reportDate).getTime() -
      new Date(a.transactionDate ?? a.reportDate).getTime(),
  );
  // Finans grafiği yalnızca muhasebe kayıtlarını kullanır; kasa/Z raporu aynı geliri tekrar saydırmaz.
  const financeByDate = new Map<string, { income: number; expense: number }>();
  for (const row of accounting.filter((item) => !item.isCancelled)) {
    const date = String(row.transactionDate ?? "").slice(0, 10);
    if (!date) continue;
    const totals = financeByDate.get(date) ?? { income: 0, expense: 0 };
    const type = enumValue(row.transactionType, "accounting");
    if (type === "Income") totals.income += Number(row.amount ?? 0);
    if (type === "Expense") totals.expense += Number(row.amount ?? 0);
    financeByDate.set(date, totals);
  }
  const chartRows = [...financeByDate.entries()]
    .map(([date, totals]) => ({ date, ...totals }))
    .sort((a, b) => a.date.localeCompare(b.date))
    .slice(-6);
  const maxAmount = Math.max(
    0,
    ...chartRows.flatMap((row) => [row.income, row.expense]),
  );
  const chartPoints = (kind: "income" | "expense") =>
    chartRows
      .map((row, index) => {
        const value = row[kind];
        const x =
          chartRows.length < 2 ? 360 : (index / (chartRows.length - 1)) * 720;
        const y = maxAmount === 0 ? 190 : 190 - (value / maxAmount) * 170;
        return `${x},${y}`;
      })
      .join(" ");
  const recentActivities = timeline.slice(0, 5).map((row) => {
    const report = row.kind === "report";
    const cash = row.kind === "cash";
    const type = enumValue(row.transactionType, cash ? "cash" : "accounting");
    return {
      icon: report ? "▤" : cash ? "◈" : "↗",
      title: report
        ? "Z raporu kaydedildi"
        : cash
          ? type === "CashOut"
            ? "Kasadan ödeme yapıldı"
            : "Kasaya para girişi"
          : type === "Expense"
            ? "Gider kaydedildi"
            : "Gelir kaydedildi",
      detail:
        row.description ??
        row.category ??
        (report ? "Z raporu" : "Finans hareketi"),
      amount: money(report ? zReportTotal(row) : row.amount),
      date: formatDate(row.transactionDate ?? row.reportDate),
    };
  });
  const cards = [
    ["Toplam müşteri", d?.totalCustomers ?? "—", "Aktif müşteri", "◉"],
    ["Toplam personel", d?.totalEmployees ?? "—", "Aktif personel", "♙"],
    ["Toplam gelir", money(d?.totalIncome), "Muhasebe toplamı", "↗"],
    ["Toplam gider", money(d?.totalExpense), "Muhasebe toplamı", "↘"],
    ["Net bakiye", money(d?.netBalance), "Gelir − gider", "₺"],
    ["Kasa bakiyesi", money(d?.currentCashBalance), "Güncel bakiye", "◈"],
  ];
  const cardDestinations = [
    "/customers",
    "/employees",
    "/accounting?type=Income",
    "/accounting?type=Expense",
    "/accounting",
    "/cash",
  ];
  return (
    <>
      <div className="stats">
        {cards.map(([a, b, c, ic], i) => (
          <Link className="stat stat-link" key={a} to={cardDestinations[i]}>
            <div className="stat-top">
              <span>{a}</span>
              <i className={"stat-icon tone" + i}>{ic}</i>
            </div>
            <strong>{b}</strong>
            <small>
              <em>{c} · Detayları görüntüle →</em>
            </small>
          </Link>
        ))}
      </div>
      {dashboardError && (
        <div className="error dashboard-error" role="alert">
          <span>{dashboardError}</span>
          <button
            className="filter-btn"
            onClick={() => {
              setDashboardError("");
              setDashboardLoading(true);
              setDashboardRefresh((value) => value + 1);
            }}
          >
            Yeniden dene
          </button>
        </div>
      )}
      <div className="grid-main">
        <article className="panel revenue">
          <div className="panel-head">
            <div>
              <h2>Finansal görünüm</h2>
              <p>
                Muhasebe kayıtlarına göre tarihler bazında gelir ve giderler
              </p>
            </div>
            <span className="filter-btn">Son 6 tarih</span>
          </div>
          {chartRows.length === 0 || maxAmount === 0 ? (
            <p className="empty chart-empty">
              {dashboardLoading
                ? "Finansal veriler yükleniyor..."
                : chartRows.length === 0
                  ? "Grafik için henüz muhasebe hareketi yok."
                  : "Gösterilecek gelir veya gider tutarı bulunmuyor."}
            </p>
          ) : (
            <div className="chart">
              <div className="y-labels">
                {[1, 0.75, 0.5, 0.25].map((ratio) => (
                  <span key={ratio}>
                    {new Intl.NumberFormat("tr-TR", {
                      notation: "compact",
                      maximumFractionDigits: 1,
                    }).format(maxAmount * ratio)}{" "}
                    ₺
                  </span>
                ))}
                <span>₺0</span>
              </div>
              <div className="chart-area">
                <div className="grid-lines">
                  <i />
                  <i />
                  <i />
                  <i />
                  <i />
                </div>
                <svg viewBox="0 0 720 210" preserveAspectRatio="none">
                  <title>
                    Tarihlere göre muhasebe gelir ve gider toplamları
                  </title>
                  <polyline
                    points={chartPoints("income")}
                    fill="none"
                    stroke="#10a37f"
                    strokeWidth="3"
                    vectorEffect="non-scaling-stroke"
                  />
                  <polyline
                    points={chartPoints("expense")}
                    fill="none"
                    stroke="#d2a35c"
                    strokeWidth="2"
                    strokeDasharray="5 6"
                    vectorEffect="non-scaling-stroke"
                  />
                  {chartRows.map((row, index) => {
                    const x =
                      chartRows.length < 2
                        ? 360
                        : (index / (chartRows.length - 1)) * 720;
                    const incomeY =
                      maxAmount === 0
                        ? 190
                        : 190 - (row.income / maxAmount) * 170;
                    const expenseY =
                      maxAmount === 0
                        ? 190
                        : 190 - (row.expense / maxAmount) * 170;
                    return (
                      <g key={row.date}>
                        <circle cx={x} cy={incomeY} r="4" fill="#10a37f">
                          <title>{`${formatDate(new Date(`${row.date}T00:00:00`))} gelir: ${money(row.income)}`}</title>
                        </circle>
                        <circle cx={x} cy={expenseY} r="4" fill="#d2a35c">
                          <title>{`${formatDate(new Date(`${row.date}T00:00:00`))} gider: ${money(row.expense)}`}</title>
                        </circle>
                      </g>
                    );
                  })}
                </svg>
                <div className="months">
                  {chartRows.map((row) => (
                    <span key={row.date}>
                      {new Date(`${row.date}T00:00:00`).toLocaleDateString(
                        "tr-TR",
                        {
                          day: "2-digit",
                          month: "short",
                        },
                      )}
                    </span>
                  ))}
                </div>
              </div>
            </div>
          )}
          <div className="legend">
            <span>
              <i />
              Gelir
            </span>
            <span>
              <i />
              Gider
            </span>
          </div>
        </article>
        <article className="panel activity">
          <div className="panel-head">
            <div>
              <h2>Son hareketler</h2>
              <p>İşletmenizdeki son güncellemeler</p>
            </div>
            <button className="more">•••</button>
          </div>
          {recentActivities.map((x, i) => (
            <div className="activity-row" key={i}>
              <div className={"activity-icon ai" + i}>{x.icon}</div>
              <div className="act-text">
                <b>{x.title}</b>
                <small>{x.detail}</small>
              </div>
              <div className="act-meta">
                <b>{x.amount}</b>
                <small>{x.date}</small>
              </div>
            </div>
          ))}
          {!recentActivities.length && (
            <p className="empty">Henüz finans hareketi yok.</p>
          )}
        </article>
      </div>
      <article className="panel table-panel">
        <div className="panel-head">
          <div>
            <h2>Son işlemler</h2>
            <p>En son finansal hareketlerinize hızlı bakış</p>
          </div>
          <Link to="/accounting" className="text-link">
            Tüm işlemleri gör →
          </Link>
        </div>
        <TransactionTable
          rows={d?.recentAccountingTransactions ?? []}
          onDetails={setSelectedTransaction}
        />
      </article>
      {selectedTransaction && (
        <Dialog
          title="İşlem detayları"
          onClose={() => setSelectedTransaction(null)}
        >
          <div className="detail-grid modal-detail-grid">
            {Object.entries(selectedTransaction)
              .filter(([key]) => key !== "id")
              .map(([key, value]) => (
                <div key={key}>
                  <small>{alanEtiketi(key)}</small>
                  <b>
                    {typeof value === "number" &&
                    /amount|salary|sales|vat|balance/i.test(key)
                      ? money(value)
                      : displayValue(key, value)}
                  </b>
                </div>
              ))}
          </div>
        </Dialog>
      )}
    </>
  );
}
function money(v: any) {
  return typeof v === "number"
    ? new Intl.NumberFormat("tr-TR", {
        style: "currency",
        currency: "TRY",
        maximumFractionDigits: 0,
      }).format(v)
    : "—";
}
function zReportTotal(row: Row) {
  return Number(row.cashAmount ?? 0) + Number(row.cardAmount ?? 0);
}
// API alan adlarını değiştirmeden ekranda Türkçe karşılıklarını gösterir.
const alanAdlari: Record<string, string> = {
  customerCode: "Müşteri kodu",
  firstName: "Ad",
  lastName: "Soyad",
  companyName: "Firma adı",
  phone: "Telefon",
  email: "E-posta",
  address: "Adres",
  taxNumber: "Vergi numarası",
  createdAt: "Kayıt tarihi",
  updatedAt: "Güncelleme tarihi",
  position: "Görev / pozisyon",
  hireDate: "İşe giriş tarihi",
  salary: "Maaş",
  transactionType: "İşlem türü",
  amount: "Tutar",
  description: "Açıklama",
  transactionDate: "İşlem tarihi",
  category: "Kategori",
  referenceNumber: "Referans numarası",
  reportDate: "Rapor tarihi",
  grossSales: "Brüt satış",
  totalVat: "Toplam KDV",
  netSales: "Net satış",
  cashAmount: "Nakit tutarı",
  cardAmount: "Kart tutarı",
  totalAmount: "Toplam tutar",
  documentPath: "Belge yolu",
  ocrRawText: "OCR ham metni",
  isConfirmed: "Onay durumu",
  isActive: "Aktif",
  isCancelled: "İptal edildi",
};
const degerAdlari: Record<string, string> = {
  Income: "Gelir",
  Expense: "Gider",
  CashIn: "Kasa girişi",
  CashOut: "Kasa çıkışı",
  Confirmed: "Onaylandı",
  Pending: "Bekliyor",
  true: "Evet",
  false: "Hayır",
};
const sayfaAdlari: Record<string, string> = {
  customers: "müşteriler",
  employees: "personel kayıtları",
  accounting: "muhasebe işlemleri",
  cash: "kasa hareketleri",
  zreports: "Z raporları",
};
function alanEtiketi(key: string) {
  return alanAdlari[key] ?? key;
}
function degeriGoster(value: unknown) {
  if (typeof value === "boolean") return degerAdlari[String(value)];
  if (typeof value === "string") return degerAdlari[value] ?? value;
  return String(value ?? "—");
}
function TransactionTable({
  rows,
  onDetails,
}: {
  rows: Row[];
  onDetails?: (row: Row) => void;
}) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>İŞLEM</th>
            <th>KATEGORİ</th>
            <th>TARİH</th>
            <th>TUTAR</th>
            <th>DURUM</th>
            {onDetails && <th>DETAY</th>}
          </tr>
        </thead>
        <tbody>
          {rows.length ? (
            rows.map((r, i) => (
              <tr key={r.id}>
                <td>
                  <span className="tx-mark">{i % 2 ? "↘" : "↗"}</span>
                  <b>{r.description}</b>
                  <small>{r.referenceNumber ?? `İŞLEM-${r.id}`}</small>
                </td>
                <td>
                  {r.category ??
                    displayValue("transactionType", r.transactionType)}
                </td>
                <td>{formatDate(r.transactionDate, true)}</td>
                <td
                  className={r.transactionType === "Income" ? "positive" : ""}
                >
                  {money(r.amount)}
                </td>
                <td>
                  <span className="status">Tamamlandı</span>
                </td>
                {onDetails && (
                  <td>
                    <button
                      className="table-detail-button"
                      onClick={() => onDetails(r)}
                    >
                      Detay
                    </button>
                  </td>
                )}
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={onDetails ? 6 : 5} className="empty">
                Henüz işlem kaydı yok. Kayıt eklediğinizde burada görüntülenir.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
function Dialog({
  title,
  children,
  onClose,
  actions,
}: {
  title: string;
  children: React.ReactNode;
  onClose: () => void;
  actions?: React.ReactNode;
}) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);
  return (
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <section
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header className="dialog-header">
          <h2>{title}</h2>
          <button className="dialog-close" aria-label="Kapat" onClick={onClose}>
            ×
          </button>
        </header>
        <div className="dialog-content">{children}</div>
        {actions && <div className="dialog-actions">{actions}</div>}
      </section>
    </div>
  );
}
function DetailPage({ title }: { title: string }) {
  const [row, setRow] = useState<Row | null>(null);
  const path = location.pathname;
  const [kind, id] = path.split("/").slice(1);
  const nav = useNavigate();
  const endpoint =
    kind === "zreports"
      ? "ZReports"
      : kind === "customers"
        ? "Customers"
        : kind === "employees"
          ? "Employees"
          : kind === "accounting"
            ? "AccountingTransactions"
            : "CashTransactions";
  useEffect(() => {
    api
      .get(`/${endpoint}/${id}`)
      .then((r) => setRow(r.data))
      .catch(() => setRow(null));
  }, [endpoint, id]);
  return (
    <article className="panel">
      <div className="panel-head">
        <div>
          <h2>{title}</h2>
          <p>Çalışma alanınızda kayıtlı bilgiler</p>
        </div>
        <div className="detail-actions">
          <button className="filter-btn" onClick={() => nav(`/${kind}`)}>
            ← Listeye Dön
          </button>
          <Link className="text-link" to={`/${kind}/${id}/edit`}>
            Kaydı düzenle →
          </Link>
        </div>
      </div>
      {row ? (
        <div className="detail-grid">
          {Object.entries(row)
            .filter(([k]) => !["id"].includes(k))
            .map(([k, v]) => (
              <div key={k}>
                <small>{alanEtiketi(k)}</small>
                <b>
                  {kind === "zreports" && k === "totalAmount"
                    ? money(zReportTotal(row))
                    : kind === "zreports" && k === "netSales"
                      ? money(
                          Number(row.grossSales ?? 0) -
                            Number(row.totalVat ?? 0),
                        )
                      : typeof v === "number" &&
                          /amount|salary|sales|vat|balance/i.test(k)
                        ? money(v)
                        : displayValue(k, v, kind)}
                </b>
              </div>
            ))}
        </div>
      ) : (
        <p className="empty">Kayıt bulunamadı.</p>
      )}
    </article>
  );
}
function DataPage() {
  const path = location.pathname;
  const segment = path.split("/")[1];
  const routeFilter = new URLSearchParams(location.search).get("type") ?? "";
  const [rows, setRows] = useState<Row[]>([]);
  const [rowsLoading, setRowsLoading] = useState(true);
  const [rowsError, setRowsError] = useState("");
  const [summary, setSummary] = useState<Row | null>(null);
  const [q, setQ] = useState("");
  const [filtersOpen, setFiltersOpen] = useState(Boolean(routeFilter));
  const [filterType, setFilterType] = useState(routeFilter);
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [deleteCandidate, setDeleteCandidate] = useState<Row | null>(null);
  const [notice, setNotice] = useState<{
    title: string;
    message: string;
  } | null>(null);
  const nav = useNavigate();
  useEffect(() => {
    const endpoint =
      segment === "zreports"
        ? "ZReports"
        : segment === "customers"
          ? "Customers"
          : segment === "employees"
            ? "Employees"
            : segment === "accounting"
              ? "AccountingTransactions"
              : "CashTransactions";
    api
      .get("/" + endpoint)
      .then((r) => {
        setRows(r.data);
        setRowsError("");
      })
      .catch(() => {
        setRows([]);
        setRowsError(
          "Kayıtlar sunucudan alınamadı. API bağlantısını kontrol edip yeniden deneyin.",
        );
      })
      .finally(() => setRowsLoading(false));
    const summaryEndpoint =
      segment === "accounting"
        ? "/AccountingTransactions/summary"
        : segment === "cash"
          ? "/CashTransactions/summary"
          : null;
    if (summaryEndpoint) {
      api
        .get(summaryEndpoint)
        .then((r) => setSummary(r.data))
        .catch(() => setSummary(null));
    }
  }, [segment]);
  const shown = rows.filter((row) => {
    if (!JSON.stringify(row).toLowerCase().includes(q.toLowerCase()))
      return false;
    const dateValue =
      row.transactionDate ?? row.reportDate ?? row.createdAt ?? row.hireDate;
    if (
      dateFrom &&
      (!dateValue || new Date(dateValue) < new Date(`${dateFrom}T00:00:00`))
    )
      return false;
    if (
      dateTo &&
      (!dateValue || new Date(dateValue) > new Date(`${dateTo}T23:59:59`))
    )
      return false;
    if (filterType === "confirmed" && !row.isConfirmed) return false;
    if (filterType === "pending" && row.isConfirmed) return false;
    if (
      filterType &&
      !["confirmed", "pending"].includes(filterType) &&
      enumValue(
        row.transactionType,
        segment === "cash" ? "cash" : "accounting",
      ) !== filterType
    )
      return false;
    return true;
  });
  function exportShownRows() {
    const columns = Object.keys(shown[0] ?? {});
    if (!columns.length) {
      setNotice({
        title: "Dışa aktarma",
        message: "Dışa aktarılacak kayıt bulunamadı.",
      });
      return;
    }
    const escapeCell = (value: unknown) =>
      `"${String(value ?? "").replaceAll('"', '""')}"`;
    const csv = [
      columns.map((column) => escapeCell(alanEtiketi(column))).join(","),
      ...shown.map((row) =>
        columns.map((column) => escapeCell(row[column])).join(","),
      ),
    ].join("\r\n");
    const url = URL.createObjectURL(
      new Blob(["\uFEFF", csv], { type: "text/csv;charset=utf-8" }),
    );
    const link = document.createElement("a");
    link.href = url;
    link.download = `${segment}-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.append(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return (
    <>
      {(segment === "accounting" || segment === "cash") && (
        <div className="stats summary-stats">
          {(segment === "accounting"
            ? [
                ["Toplam gelir", summary?.totalIncome],
                ["Toplam gider", summary?.totalExpense],
                ["Net bakiye", summary?.netBalance],
              ]
            : [
                ["Toplam kasa girişi", summary?.totalCashIn],
                ["Toplam kasa çıkışı", summary?.totalCashOut],
                ["Güncel kasa bakiyesi", summary?.currentBalance],
              ]
          ).map(([label, amount]) => (
            <article className="stat" key={label}>
              <div className="stat-top">
                <span>{label}</span>
              </div>
              <strong>{amount == null ? "—" : money(amount)}</strong>
            </article>
          ))}
        </div>
      )}
      <div className="panel list-panel">
        <div className="list-tools">
          <div className="filter-search">
            ⌕{" "}
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder={`${sayfaAdlari[segment] ?? "kayıtlar"} içinde ara...`}
            />
          </div>
          <button
            className="filter-btn"
            onClick={() => setFiltersOpen((open) => !open)}
          >
            ☷ Filtreler
          </button>
          <button className="filter-btn" onClick={exportShownRows}>
            ↓ Dışa aktar
          </button>
        </div>
        {filtersOpen && (
          <div className="filter-panel">
            {(segment === "accounting" || segment === "cash") && (
              <label>
                İşlem türü
                <select
                  value={filterType}
                  onChange={(event) => setFilterType(event.target.value)}
                >
                  <option value="">Tüm türler</option>
                  {(segment === "accounting"
                    ? [
                        ["Income", "Gelir"],
                        ["Expense", "Gider"],
                      ]
                    : [
                        ["CashIn", "Kasa girişi"],
                        ["CashOut", "Kasa çıkışı"],
                      ]
                  ).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {segment === "zreports" && (
              <label>
                Onay durumu
                <select
                  value={filterType}
                  onChange={(event) => setFilterType(event.target.value)}
                >
                  <option value="">Tüm durumlar</option>
                  <option value="confirmed">Onaylandı</option>
                  <option value="pending">Bekliyor</option>
                </select>
              </label>
            )}
            <label>
              Başlangıç tarihi
              <input
                type="date"
                value={dateFrom}
                onChange={(event) => setDateFrom(event.target.value)}
              />
            </label>
            <label>
              Bitiş tarihi
              <input
                type="date"
                value={dateTo}
                onChange={(event) => setDateTo(event.target.value)}
              />
            </label>
            <button
              className="filter-btn"
              onClick={() => {
                setFilterType("");
                setDateFrom("");
                setDateTo("");
              }}
            >
              Filtreleri temizle
            </button>
          </div>
        )}
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                {(segment === "customers"
                  ? ["Müşteri", "Firma", "Telefon", "E-posta", "Kayıt tarihi"]
                  : segment === "employees"
                    ? [
                        "Personel",
                        "Görev",
                        "Telefon",
                        "E-posta",
                        "İşe giriş tarihi",
                      ]
                    : segment === "accounting"
                      ? ["İşlem", "Tür", "Kategori", "Tarih", "Tutar"]
                      : segment === "cash"
                        ? ["İşlem", "Tür", "Referans", "Tarih", "Tutar"]
                        : [
                            "Rapor tarihi",
                            "Brüt satış",
                            "Net satış",
                            "KDV",
                            "Nakit",
                            "Kart",
                            "Toplam",
                            "Durum",
                          ]
                ).map((x) => (
                  <th key={x}>{x.toUpperCase()}</th>
                ))}
                <th>İŞLEMLER</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((r) => (
                <tr key={r.id}>
                  {segment === "customers" ? (
                    <>
                      <td>
                        <b>
                          {capitalizeWords(r.firstName)}{" "}
                          {capitalizeWords(r.lastName)}
                        </b>
                        <small>{r.customerCode}</small>
                      </td>
                      <td>{r.companyName || "—"}</td>
                      <td>{r.phone}</td>
                      <td>{r.email}</td>
                      <td>{formatDate(r.createdAt)}</td>
                    </>
                  ) : segment === "employees" ? (
                    <>
                      <td>
                        <b>
                          {capitalizeWords(r.firstName)}{" "}
                          {capitalizeWords(r.lastName)}
                        </b>
                      </td>
                      <td>{r.position}</td>
                      <td>{r.phone}</td>
                      <td>{r.email}</td>
                      <td>{formatDate(r.hireDate)}</td>
                    </>
                  ) : segment === "zreports" ? (
                    <>
                      <td>{formatDate(r.reportDate)}</td>
                      <td>{money(r.grossSales)}</td>
                      <td>
                        {money(
                          Number(r.grossSales ?? 0) - Number(r.totalVat ?? 0),
                        )}
                      </td>
                      <td>{money(r.totalVat)}</td>
                      <td>{money(r.cashAmount)}</td>
                      <td>{money(r.cardAmount)}</td>
                      <td>{money(zReportTotal(r))}</td>
                      <td>
                        <span className="status">
                          {r.isConfirmed ? "Onaylandı" : "Bekliyor"}
                        </span>
                      </td>
                    </>
                  ) : (
                    <>
                      <td>
                        <b>{r.description}</b>
                        <small>{r.referenceNumber}</small>
                      </td>
                      <td>
                        {displayValue(
                          "transactionType",
                          r.transactionType,
                          segment,
                        )}
                      </td>
                      <td>{r.category ?? "—"}</td>
                      <td>{formatDate(r.transactionDate, true)}</td>
                      <td className="positive">{money(r.amount)}</td>
                    </>
                  )}
                  <td className="row-actions">
                    <button onClick={() => nav(`/${segment}/${r.id}`)}>
                      Görüntüle
                    </button>
                    <button onClick={() => nav(`/${segment}/${r.id}/edit`)}>
                      Düzenle
                    </button>
                    <button onClick={() => setDeleteCandidate(r)}>Sil</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rowsError ? (
            <div className="empty error-state" role="alert">
              {rowsError}{" "}
              <button
                className="filter-btn"
                onClick={() => window.location.reload()}
              >
                Yeniden dene
              </button>
            </div>
          ) : rowsLoading ? (
            <div className="empty" role="status">
              Kayıtlar yükleniyor...
            </div>
          ) : (
            shown.length === 0 && (
              <div className="empty">
                Kayıt bulunamadı. Başlamak için yeni bir kayıt ekleyin.
              </div>
            )
          )}
        </div>
        <div className="pagination">
          <b>{shown.length}</b> kayıt gösteriliyor{" "}
          <span>
            Sayfa başına satır <button>10⌄</button> <button>←</button>{" "}
            <button>→</button>
          </span>
        </div>
      </div>
      {deleteCandidate && (
        <Dialog
          title="Kaydı sil"
          onClose={() => setDeleteCandidate(null)}
          actions={
            <>
              <button
                className="filter-btn"
                onClick={() => setDeleteCandidate(null)}
              >
                Vazgeç
              </button>
              <button
                className="danger-button"
                onClick={async () => {
                  const url =
                    segment === "zreports"
                      ? "ZReports"
                      : segment === "customers"
                        ? "Customers"
                        : segment === "employees"
                          ? "Employees"
                          : segment === "accounting"
                            ? "AccountingTransactions"
                            : "CashTransactions";
                  try {
                    await api.delete(`/${url}/${deleteCandidate.id}`);
                    setRows((current) =>
                      current.filter((row) => row.id !== deleteCandidate.id),
                    );
                    setDeleteCandidate(null);
                    setNotice({
                      title: "Silme işlemi tamamlandı",
                      message: "Kayıt başarıyla silindi.",
                    });
                  } catch {
                    setDeleteCandidate(null);
                    setNotice({
                      title: "Silme işlemi başarısız",
                      message:
                        "Kayıt silinemedi. Bağlantıyı kontrol edip yeniden deneyin.",
                    });
                  }
                }}
              >
                Sil
              </button>
            </>
          }
        >
          <p>
            “
            {deleteCandidate.description ??
              (`${deleteCandidate.firstName ?? ""} ${deleteCandidate.lastName ?? ""}`.trim() ||
                "Seçili kayıt")}
            ” kaydını silmek istediğinize emin misiniz?
          </p>
          <p className="dialog-muted">
            Bu işlem kaydın listeden kaldırılmasına neden olur.
          </p>
        </Dialog>
      )}
      {notice && (
        <Dialog
          title={notice.title}
          onClose={() => setNotice(null)}
          actions={
            <button className="primary" onClick={() => setNotice(null)}>
              Tamam
            </button>
          }
        >
          <p>{notice.message}</p>
        </Dialog>
      )}
    </>
  );
}
function FormPage() {
  const path = location.pathname;
  const kind = path.split("/")[1];
  const editId =
    path.split("/")[2] && path.split("/")[2] !== "new"
      ? path.split("/")[2]
      : null;
  const [values, setValues] = useState<Row>({});
  const [saveNotice, setSaveNotice] = useState<{
    title: string;
    message: string;
  } | null>(null);
  const nav = useNavigate();
  const endpoint =
    kind === "zreports"
      ? "ZReports"
      : kind === "customers"
        ? "Customers"
        : kind === "employees"
          ? "Employees"
          : kind === "accounting"
            ? "AccountingTransactions"
            : "CashTransactions";
  useEffect(() => {
    if (editId)
      api
        .get(`/${endpoint}/${editId}`)
        .then((r) => setValues(r.data))
        .catch(() => {});
  }, [editId, endpoint]);
  const labels =
    kind === "customers"
      ? [
          "customerCode",
          "firstName",
          "lastName",
          "companyName",
          "phone",
          "email",
          "address",
          "taxNumber",
          "isActive",
        ]
      : kind === "employees"
        ? [
            "firstName",
            "lastName",
            "phone",
            "email",
            "position",
            "hireDate",
            "salary",
            "isActive",
          ]
        : kind === "accounting"
          ? [
              "transactionType",
              "amount",
              "description",
              "transactionDate",
              "category",
              "referenceNumber",
            ]
          : kind === "cash"
            ? [
                "transactionType",
                "amount",
                "description",
                "transactionDate",
                "referenceNumber",
              ]
            : [
                "reportDate",
                "grossSales",
                "totalVat",
                "netSales",
                "cashAmount",
                "cardAmount",
                "totalAmount",
                "ocrRawText",
              ];
  async function save(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const d = Object.fromEntries(new FormData(e.currentTarget)) as Record<
      string,
      any
    >;
    if (d.firstName) d.firstName = capitalizeWords(d.firstName);
    if (d.lastName) d.lastName = capitalizeWords(d.lastName);
    if (kind === "customers" || kind === "employees") {
      d.isActive = new FormData(e.currentTarget).has("isActive");
    }
    if (kind === "zreports") d.isConfirmed = true;
    if (kind === "zreports") {
      d.netSales = Number(d.grossSales ?? 0) - Number(d.totalVat ?? 0);
      d.totalAmount = Number(d.cashAmount ?? 0) + Number(d.cardAmount ?? 0);
    }
    try {
      if (editId) await api.put(`/${endpoint}/${editId}`, d);
      else await api.post("/" + endpoint, d);
      setSaveNotice({
        title: "Kayıt tamamlandı",
        message: "Bilgiler başarıyla kaydedildi.",
      });
    } catch {
      setSaveNotice({
        title: "Kayıt başarısız",
        message: "Bilgiler kaydedilemedi. Alanları ve bağlantıyı kontrol edin.",
      });
    }
  }
  return (
    <>
      <form
        key={JSON.stringify(values)}
        className="panel edit-form"
        onSubmit={save}
      >
        <div className="form-topline">
          <button
            type="button"
            className="filter-btn"
            onClick={() => nav(`/${kind}`)}
          >
            ← Listeye Dön
          </button>
        </div>
        <div className="form-grid">
          {labels.map((k) => (
            <label key={k}>
              {alanEtiketi(k)}
              {k === "isActive" ? (
                <span className="checkbox-field">
                  <input
                    type="checkbox"
                    name={k}
                    defaultChecked={values[k] ?? true}
                  />
                  <span>Bu kaydı etkin tut</span>
                </span>
              ) : k === "transactionType" ? (
                <select
                  name={k}
                  defaultValue={
                    enumValue(
                      values[k] ?? (kind === "cash" ? "CashIn" : "Income"),
                      kind === "cash" ? "cash" : "accounting",
                    ) as string
                  }
                >
                  {kind === "cash" ? (
                    <>
                      <option value="CashIn">Kasa girişi</option>
                      <option value="CashOut">Kasa çıkışı</option>
                    </>
                  ) : (
                    <>
                      <option value="Income">Gelir</option>
                      <option value="Expense">Gider</option>
                    </>
                  )}
                </select>
              ) : (
                <input
                  name={k}
                  onBlur={
                    k === "firstName" || k === "lastName"
                      ? (event) => {
                          event.currentTarget.value = capitalizeWords(
                            event.currentTarget.value,
                          );
                        }
                      : undefined
                  }
                  defaultValue={
                    k === "netSales" && values.grossSales != null
                      ? Number(values.grossSales) - Number(values.totalVat ?? 0)
                      : k === "totalAmount" && values.cashAmount != null
                        ? Number(values.cashAmount) +
                          Number(values.cardAmount ?? 0)
                        : k.toLowerCase().includes("date") && values[k]
                          ? String(values[k]).slice(0, 10)
                          : (values[k] ?? "")
                  }
                  required={
                    ![
                      "companyName",
                      "taxNumber",
                      "referenceNumber",
                      "ocrRawText",
                    ].includes(k)
                  }
                  type={
                    k.toLowerCase().includes("date")
                      ? "date"
                      : k.toLowerCase().includes("amount") ||
                          k === "salary" ||
                          k.startsWith("gross") ||
                          k.startsWith("total") ||
                          k === "netSales"
                        ? "number"
                        : "text"
                  }
                  step="0.01"
                />
              )}
            </label>
          ))}
        </div>
        <div className="form-actions">
          <button type="button" className="filter-btn" onClick={() => nav(-1)}>
            Vazgeç
          </button>
          <button className="primary">Kaydet →</button>
        </div>
      </form>
      {saveNotice && (
        <Dialog
          title={saveNotice.title}
          onClose={() => {
            const success = saveNotice.title === "Kayıt tamamlandı";
            setSaveNotice(null);
            if (success) nav("/" + kind);
          }}
          actions={
            <button
              className="primary"
              onClick={() => {
                const success = saveNotice.title === "Kayıt tamamlandı";
                setSaveNotice(null);
                if (success) nav("/" + kind);
              }}
            >
              Tamam
            </button>
          }
        >
          <p>{saveNotice.message}</p>
        </Dialog>
      )}
    </>
  );
}
function Upload() {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<Row | null>(null);
  const [isReading, setIsReading] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [previewUrl, setPreviewUrl] = useState("");
  const [notice, setNotice] = useState<{
    title: string;
    message: string;
  } | null>(null);
  const nav = useNavigate();
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") nav("/zreports");
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [nav]);
  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);
  function chooseFile(candidate?: File) {
    if (!candidate) return;
    const allowed = ["image/png", "image/jpeg", "application/pdf"];
    if (
      !allowed.includes(candidate.type) ||
      candidate.size > 10 * 1024 * 1024
    ) {
      setNotice({
        title: "Dosya uygun değil",
        message: "10 MB altında PNG, JPG veya PDF dosyası seçin.",
      });
      return;
    }
    setPreviewUrl(
      candidate.type.startsWith("image/") ? URL.createObjectURL(candidate) : "",
    );
    setFile(candidate);
    setResult(null);
  }
  async function read() {
    if (!file || isReading) return;
    const form = new FormData();
    form.append("file", file);
    setIsReading(true);
    try {
      const { data } = await api.post("/ZReports/ocr", form);
      setResult(data);
    } catch (error: any) {
      const ocrResult = error.response?.data as Row | undefined;
      if (ocrResult?.documentPath) setResult(ocrResult);
      const providerUnavailable = error.response?.status === 503;
      setNotice({
        title: providerUnavailable
          ? "OCR hizmeti kullanılamıyor"
          : ocrResult?.documentPath
            ? "Belge OCR ile okunamadı"
            : "Dosya okunamadı",
        message: ocrResult?.documentPath
          ? providerUnavailable
            ? "Dosya güvenli biçimde saklandı. OCR kurulumu tamamlanana kadar tutarları gerçek raporunuzdan elle girin."
            : "Dosya güvenli biçimde saklandı ancak OCR içeriği okuyamadı. Tutarları gerçek raporunuzdan elle girin."
          : "OCR isteği başarısız oldu. Dosya türünü ve API bağlantısını kontrol edin.",
      });
    } finally {
      setIsReading(false);
    }
  }
  async function save(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const d = Object.fromEntries(new FormData(e.currentTarget)) as Record<
      string,
      any
    >;
    for (const k of [
      "grossSales",
      "totalVat",
      "netSales",
      "cashAmount",
      "cardAmount",
      "totalAmount",
    ])
      d[k] = Number(d[k] || 0);
    d.documentPath = result?.documentPath;
    d.ocrRawText = result?.ocrRawText;
    d.isConfirmed = true;
    try {
      await api.post("/ZReports", d);
      nav("/zreports");
    } catch {
      setNotice({
        title: "Z raporu kaydedilemedi",
        message:
          "Rapor bilgileri kaydedilemedi. Tutarları kontrol edip yeniden deneyin.",
      });
    }
  }
  return (
    <>
      <div
        className="upload-modal-backdrop"
        onMouseDown={(e) => e.target === e.currentTarget && nav("/zreports")}
      >
        <div
          className="panel upload-box"
          role="dialog"
          aria-modal="true"
          aria-label="Yeni Z Raporu Yükle"
        >
          <div className="upload-modal-heading">
            <h2>Yeni Z Raporu Yükle</h2>
          </div>
          <button
            className="upload-close"
            aria-label="Pencereyi kapat"
            onClick={() => nav("/zreports")}
          >
            ×
          </button>
          <label
            className={`dropzone ${isDragging ? "dragging" : ""}`}
            onDragOver={(event) => {
              event.preventDefault();
              setIsDragging(true);
            }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={(event) => {
              event.preventDefault();
              setIsDragging(false);
              chooseFile(event.dataTransfer.files[0]);
            }}
          >
            ▧<h2>Z raporu dosyası seçin</h2>
            <p>Dosyayı buraya sürükleyin veya seçmek için tıklayın</p>
            <small>PNG, JPG veya PDF · En fazla 10 MB</small>
            <input
              type="file"
              accept=".png,.jpg,.jpeg,.pdf"
              onChange={(e) => chooseFile(e.target.files?.[0])}
            />
          </label>
          {file && (
            <div className="file-preview">
              {previewUrl ? (
                <img src={previewUrl} alt="Yüklenen Z raporu ön izlemesi" />
              ) : (
                <div className="pdf-preview">PDF</div>
              )}
              <div>
                <b>{file.name}</b>
                <small>{(file.size / 1024).toFixed(0)} KB</small>
              </div>
              <button
                type="button"
                className="filter-btn"
                onClick={() => {
                  setFile(null);
                  setResult(null);
                }}
              >
                Kaldır
              </button>
            </div>
          )}
          {result && !result.grossSales && !result.totalAmount && (
            <div className="ocr-demo-warning" role="status">
              <b>
                {result.ocrRawText?.startsWith("OCR çalıştırılamadı")
                  ? "OCR hizmeti kullanılamadı."
                  : "Belgede tutar bulunamadı."}
              </b>
              <span>
                {result.ocrRawText ||
                  "OCR ham metnini inceleyin ve rapordaki gerçek tutarları elle girin. Örnek tutar kullanılmadı."}
              </span>
            </div>
          )}
          <button
            className="primary"
            disabled={!file || isReading}
            onClick={read}
          >
            {isReading && <i className="loading-spinner" />}
            {isReading ? "Dosya okunuyor..." : "OCR işlemini başlat →"}
          </button>
          {result && (
            <form className="ocr-review" onSubmit={save}>
              <h2>Okunan bilgileri inceleyin</h2>
              <p>
                Raporu onaylamadan önce tutarları kontrol edip gerekirse
                düzeltin.
              </p>
              <label>
                Rapor tarihi
                <input
                  type="date"
                  name="reportDate"
                  required
                  defaultValue={new Date().toISOString().slice(0, 10)}
                />
              </label>
              <div className="form-grid">
                {[
                  ["grossSales", "Brüt satış"],
                  ["totalVat", "Toplam KDV"],
                  ["netSales", "Net satış"],
                  ["cashAmount", "Nakit tutarı"],
                  ["cardAmount", "Kart tutarı"],
                  ["totalAmount", "Toplam tutar"],
                ].map(([key, label]) => (
                  <label key={key}>
                    {label}
                    <input
                      name={key}
                      type="number"
                      min="0"
                      step="0.01"
                      required
                      defaultValue={result[key] ?? ""}
                    />
                  </label>
                ))}
              </div>
              <details>
                <summary>OCR ham metni</summary>
                <pre>
                  {result.ocrRawText ||
                    "OCR sağlayıcısı yapılandırılmadığı için metin okunamadı."}
                </pre>
              </details>
              <div className="form-actions">
                <button className="primary">Onayla ve kaydet →</button>
              </div>
            </form>
          )}
          <p className="hint">
            OCR sonuçları öneri niteliğindedir. Kaydetmeden önce yönetici
            tarafından kontrol edilip onaylanmalıdır.
          </p>
        </div>
      </div>
      {notice && (
        <Dialog
          title={notice.title}
          onClose={() => setNotice(null)}
          actions={
            <button className="primary" onClick={() => setNotice(null)}>
              Tamam
            </button>
          }
        >
          <p>{notice.message}</p>
        </Dialog>
      )}
    </>
  );
}
function Profile() {
  const [p, setP] = useState<Row>();
  useEffect(() => {
    api
      .get("/Auth/me")
      .then((r) => setP(r.data))
      .catch(() => {});
  }, []);
  return (
    <div className="panel profile">
      <div className="avatar large">{p?.firstName?.[0] ?? "A"}</div>
      <div>
        <h2>
          {p?.firstName ? capitalizeWords(p.firstName) : ""}{" "}
          {p?.lastName ? capitalizeWords(p.lastName) : ""}
        </h2>
        <p>{p?.email ?? "Yönetici profili"}</p>
        <span className="status">Etkin yönetici</span>
      </div>
    </div>
  );
}
export default App;
