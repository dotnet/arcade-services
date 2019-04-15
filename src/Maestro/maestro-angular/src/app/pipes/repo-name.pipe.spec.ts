import { RepoNamePipe } from './repo-name.pipe';

describe('RepoNamePipe', () => {
  it('create an instance', () => {
    const pipe = new RepoNamePipe();
    expect(pipe).toBeTruthy();
  });
});
