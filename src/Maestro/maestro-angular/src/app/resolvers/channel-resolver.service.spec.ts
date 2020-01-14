import { TestBed } from '@angular/core/testing';

import { ChannelResolverService } from './channel-resolver.service';
import { ChannelService } from '../services/channel.service';

describe('ChannelResolverService', () => {
  beforeEach(() => {
    const mockChannelService = jasmine.createSpyObj("ChannelService", ["getChannels"]);
    TestBed.configureTestingModule({
      providers: [
        { provide: ChannelService, useValue: mockChannelService },
      ],
    });
  });

  it('should be created', () => {
    const service: ChannelResolverService = TestBed.get(ChannelResolverService);
    expect(service).toBeTruthy();
  });
});
