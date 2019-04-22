import { TestBed } from '@angular/core/testing';

import { ChannelResolverService } from './channel-resolver.service';

describe('ChannelResolverService', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('should be created', () => {
    const service: ChannelResolverService = TestBed.get(ChannelResolverService);
    expect(service).toBeTruthy();
  });
});
