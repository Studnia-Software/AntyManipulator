import { SearchFooter, SearchInput, SearchTitle } from '../components';

export const SearchPage = () => {
  return (
    <div className="flex flex-col min-h-screen">
      <main className="w-max  bg-white mx-auto px-5">
        <div className="max-w-2xl flex flex-col items-center mt-[16vh] sm:mt-[20vh] ">
          <SearchTitle />
          <SearchInput />
        </div>
      </main>
      <SearchFooter />
    </div>
  );
};
