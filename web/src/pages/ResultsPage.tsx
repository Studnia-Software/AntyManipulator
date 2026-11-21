import { useNavigate, useSearchParams } from 'react-router';
import { useState } from 'react';

const demoResult = {
  score: 78,
  risk: 'Wysokie ryzyko',

  analyzedText:
    'Jeżeli nie podejmiesz działania już dziś, stracisz wszystko, na co pracowałeś przez lata!',

  techniques: [
    {
      name: 'Manipulacja strachem',
      level: 'Wysoka',
      description:
        'Wywoływanie strachu poprzez przedstawianie negatywnych konsekwencji.',
      color: 'red',
    },
    {
      name: 'Presja czasu',
      level: 'Średnia',
      description:
        'Sugerowanie konieczności natychmiastowego podjęcia decyzji.',
      color: 'yellow',
    },
    {
      name: 'Katastrofizacja',
      level: 'Wysoka',
      description:
        'Przedstawianie skrajnie negatywnego scenariusza jako możliwego następstwa.',
      color: 'red',
    },
  ],

  fragments: [
    {
      text: 'Stracisz wszystko',
      technique: 'Katastrofizacja',
      explanation:
        'Autor przedstawia skrajny, negatywny scenariusz bez przedstawienia dowodów.',
      level: 'Wysoka',
    },
    {
      text: 'Już dziś',
      technique: 'Presja czasu',
      explanation:
        'Sformułowanie sugeruje, że odbiorca musi działać natychmiast.',
      level: 'Średnia',
    },
    {
      text: 'Jeżeli nie podejmiesz działania',
      technique: 'Manipulacja strachem',
      explanation:
        'Treść buduje poczucie zagrożenia i wykorzystuje lęk przed konsekwencjami.',
      level: 'Wysoka',
    },
  ],

  summary:
    'Analizowana treść wykorzystuje język emocjonalny, katastroficzne konsekwencje oraz presję czasu. Takie zabiegi mogą ograniczać zdolność odbiorcy do spokojnej oceny sytuacji. Warto zweryfikować przedstawione zagrożenie i poszukać niezależnych źródeł informacji.',
};

const colors = {
  red: {
    badge: 'bg-red-50 text-red-700',
    border: 'border-red-200',
    background: 'bg-red-50/60',
    dot: 'bg-red-500',
  },
  yellow: {
    badge: 'bg-amber-50 text-amber-700',
    border: 'border-amber-200',
    background: 'bg-amber-50/60',
    dot: 'bg-amber-500',
  },
};

// ------

function RiskBadge({ level }) {
  const color =
    level === 'Wysoka'
      ? 'bg-red-50 text-red-700'
      : 'bg-amber-50 text-amber-700';

  return (
    <span className={`px-3 py-1 rounded-full text-xs font-medium ${color}`}>
      {level}
    </span>
  );
}

function ScoreCircle({ score }) {
  return (
    <div
      className="relative w-32 h-32 rounded-full flex items-center justify-center shrink-0"
      style={{
        background: `conic-gradient(#EA4335 ${score * 3.6}deg, #F1F3F4 0deg)`,
      }}
    >
      <div className="w-28 h-28 bg-white rounded-full flex flex-col items-center justify-center">
        <span className="text-3xl font-semibold text-gray-900">{score}%</span>
        <span className="text-xs text-gray-400">wskaźnik</span>
      </div>
    </div>
  );
}

//---------------

export const ResultsPage = ({ result = demoResult }) => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const searchQuery = searchParams.get('q');
  const [expanded, setExpanded] = useState(null);

  const onBack = () => {
    navigate('/');
  };

  if (!searchQuery) {
    navigate('/');
  }

  return (
    <main className="min-h-screen bg-[#F8F9FA] text-gray-800">
      {/* HEADER */}
      <header className="bg-white border-b border-gray-200">
        <div className="max-w-5xl mx-auto px-5 py-4 flex items-center justify-between gap-4">
          <button
            onClick={onBack}
            className="text-xl font-medium tracking-tight"
            aria-label="Wróć do wyszukiwarki"
          >
            <span className="text-[#4285F4]">!</span>
            <span className="text-[#EA4335]">A</span>
            <span className="text-[#FBBC05]">n</span>
            <span className="text-[#4285F4]">t</span>
            <span className="text-[#34A853]">y</span>
            <span className="text-[#EA4335]">m</span>
            <span className="text-[#4285F4]">a</span>
            <span className="text-[#FBBC05]">n</span>
            <span className="text-[#34A853]">i</span>
            <span className="text-[#EA4335]">p</span>
            <span className="text-[#4285F4]">u</span>
            <span className="text-[#FBBC05]">l</span>
            <span className="text-[#34A853]">a</span>
            <span className="text-[#EA4335]">t</span>
            <span className="text-[#4285F4]">o</span>
            <span className="text-[#34A853]">r</span>
          </button>

          <button
            onClick={onBack}
            className="text-sm text-gray-500 hover:text-blue-600"
          >
            ← Nowa analiza
          </button>
        </div>
      </header>

      <div className="max-w-5xl mx-auto px-5 py-8 space-y-6">
        {/* TITLE */}
        <div>
          <p className="text-xs text-gray-400 uppercase tracking-wider mb-2">
            Raport analizy
          </p>

          <h1 className="text-2xl sm:text-3xl font-semibold">Wyniki analizy</h1>

          <p className="text-sm text-gray-500 mt-2">
            Poniżej znajdziesz szczegółową analizę przesłanej treści.
          </p>
        </div>

        {/* ANALYZED TEXT */}
        <section className="bg-white border border-gray-200 rounded-2xl p-5">
          <p className="text-xs font-medium text-gray-400 mb-3">
            ANALIZOWANA TREŚĆ
          </p>

          {/* <p className="text-gray-700 leading-relaxed">{result.analyzedText}</p> */}
          <p className="text-gray-700 leading-relaxed">{searchQuery}</p>
        </section>

        {/* SCORE */}
        <section className="bg-white border border-gray-200 rounded-2xl p-6 sm:p-8">
          <h2 className="text-lg font-semibold mb-6">Ogólna ocena</h2>

          <div className="flex flex-col sm:flex-row items-center gap-7">
            <ScoreCircle score={result.score} />

            <div className="text-center sm:text-left">
              <h3 className="text-xl font-semibold text-red-600">
                {result.risk}
              </h3>

              <p className="text-sm text-gray-500 mt-2 max-w-lg leading-relaxed">
                Analiza wykazała sygnały mogące wskazywać na zastosowanie
                technik perswazyjnych i manipulacyjnych.
              </p>

              <p className="text-xs text-gray-400 mt-3">
                Wskaźnik sygnałów manipulacyjnych: {result.score}/100
              </p>
            </div>
          </div>
        </section>

        {/* TECHNIQUES */}
        <section className="bg-white border border-gray-200 rounded-2xl p-6">
          <h2 className="text-lg font-semibold mb-2">
            Wykryte techniki manipulacji
          </h2>

          <p className="text-sm text-gray-500 mb-5">
            Techniki zidentyfikowane podczas analizy treści.
          </p>

          <div className="space-y-3">
            {result.techniques.map((item, index) => (
              <div
                key={index}
                className="border border-gray-100 rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3"
              >
                <div className="flex items-start gap-3">
                  <span
                    className={`w-2.5 h-2.5 rounded-full mt-1.5 ${
                      colors[item.color]?.dot || 'bg-gray-400'
                    }`}
                  />

                  <div>
                    <h3 className="font-medium text-sm">{item.name}</h3>

                    <p className="text-xs text-gray-500 mt-1">
                      {item.description}
                    </p>
                  </div>
                </div>

                <RiskBadge level={item.level} />
              </div>
            ))}
          </div>
        </section>

        {/* FRAGMENTS */}
        <section className="bg-white border border-gray-200 rounded-2xl p-6">
          <h2 className="text-lg font-semibold mb-2">Analiza fragmentów</h2>

          <p className="text-sm text-gray-500 mb-5">
            Fragmenty tekstu, które mogą wskazywać na manipulację.
          </p>

          <div className="space-y-3">
            {result.fragments.map((item, index) => {
              const isOpen = expanded === index;

              return (
                <div
                  key={index}
                  className="border border-gray-200 rounded-xl overflow-hidden"
                >
                  <button
                    onClick={() => setExpanded(isOpen ? null : index)}
                    className="w-full p-4 text-left flex items-center justify-between gap-3 hover:bg-gray-50"
                  >
                    <div>
                      <p className="font-medium text-sm">„{item.text}”</p>

                      <p className="text-xs text-gray-500 mt-1">
                        {item.technique}
                      </p>
                    </div>

                    <div className="flex items-center gap-3 shrink-0">
                      <RiskBadge level={item.level} />

                      <span className="text-gray-400">
                        {isOpen ? '−' : '+'}
                      </span>
                    </div>
                  </button>

                  {isOpen && (
                    <div className="px-4 pb-4 pt-1 bg-gray-50 border-t border-gray-100">
                      <p className="text-xs font-medium text-gray-500 mb-1">
                        Dlaczego ten fragment został oznaczony?
                      </p>

                      <p className="text-sm text-gray-600 leading-relaxed">
                        {item.explanation}
                      </p>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </section>

        {/* SUMMARY */}
        <section className="bg-white border border-gray-200 rounded-2xl p-6">
          <div className="flex items-center gap-2 mb-4">
            <span className="text-blue-500 text-xl">✦</span>

            <h2 className="text-lg font-semibold">Podsumowanie analizy</h2>
          </div>

          <p className="text-sm text-gray-600 leading-7">{result.summary}</p>

          <div className="mt-5 p-4 bg-blue-50 rounded-xl">
            <p className="text-xs text-blue-800 leading-relaxed">
              <strong>Pamiętaj:</strong> wykrycie technik manipulacyjnych nie
              oznacza automatycznie, że przedstawione informacje są
              nieprawdziwe. Wynik analizy jest wskazówką wymagającą samodzielnej
              weryfikacji.
            </p>
          </div>
        </section>

        {/* FOOTER */}
        <footer className="text-center text-xs text-gray-400 py-6">
          !Antymanipulator · Raport analizy
        </footer>
      </div>
    </main>
  );
};
