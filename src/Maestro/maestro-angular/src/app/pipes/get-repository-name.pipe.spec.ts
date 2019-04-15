import { GetRepositoryNamePipe } from './get-repository-name.pipe';

describe('GetRepositoryNamePipe', () => {
  it('create an instance', () => {
    const pipe = new GetRepositoryNamePipe();
    expect(pipe).toBeTruthy();
  });
});
