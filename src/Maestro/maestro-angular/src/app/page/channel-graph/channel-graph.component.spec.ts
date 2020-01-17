import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { ChannelGraphComponent } from './channel-graph.component';

describe('ChannelGraphComponent', () => {
  let component: ChannelGraphComponent;
  let fixture: ComponentFixture<ChannelGraphComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ ChannelGraphComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(ChannelGraphComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
