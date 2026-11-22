import { Route, Routes } from 'react-router';
import { SearchPage } from './pages/SearchPage';
import { ResultPage } from './pages/ResultPage';

function App() {
  return (
    <Routes>
      <Route path="/" element={<SearchPage />} />
      <Route path="/result" element={<ResultPage />} />
      <Route path="*" element={<div>404 Not Found</div>} />
    </Routes>
  );
}

export default App;
