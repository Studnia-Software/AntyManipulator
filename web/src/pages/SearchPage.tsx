import { SearchFooter, SearchInput, SearchTitle } from '../components';

export const SearchPage = () => {
  return (
    <div className="flex flex-col min-h-screen">
      <main className="w-full bg-white mx-auto px-4 sm:px-5">
        <div className="mx-auto w-full max-w-4xl flex flex-col items-center mt-[16vh] sm:mt-[20vh]">
          <SearchTitle />
          <SearchInput />
        </div>
      </main>
      <SearchFooter />
    </div>
  );
};
