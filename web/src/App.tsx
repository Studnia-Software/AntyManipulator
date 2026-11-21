import { Route, Routes } from 'react-router';
import { SearchPage } from './pages/SearchPage';
import { ResultsPage } from './pages/ResultsPage';

function App() {
  return (
    <Routes>
      <Route path="/" element={<SearchPage />} />
      <Route path="/result" element={<ResultsPage />} />
      <Route path="*" element={<div>404 Not Found</div>} />
    </Routes>
  );
}

export default App;
